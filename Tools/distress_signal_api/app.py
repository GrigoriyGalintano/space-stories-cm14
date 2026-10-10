"""Small, independent store for Distress Signal. Run with uvicorn app:app."""

from contextlib import asynccontextmanager, contextmanager
from datetime import datetime, timezone
import hmac
import json
import math
import os
from pathlib import Path
import sqlite3
from typing import Annotated

from fastapi import Depends, FastAPI, Header, HTTPException
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class Request(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, extra="forbid", allow_inf_nan=False)


class Load(Request):
    initial_marines_per_xeno: float = Field(gt=0)
    recent_planet_count: int = Field(ge=0, le=10000)


class History(Request):
    count: int = Field(ge=0, le=10000)


PlanetId = Annotated[str, Field(min_length=1, max_length=200)]
Votes = Annotated[int, Field(strict=True, ge=0, le=2147483647)]


class Voting(Request):
    selected_planet_id: PlanetId | None
    carryover_votes: dict[PlanetId, Votes] = Field(max_length=10000)


class Balance(Request):
    marines_per_xeno: float = Field(gt=0)


class StartRound(Balance):
    planet_id: PlanetId


class FinishRound(Balance):
    result: int = Field(ge=1, le=6)


def create_app(database: Path, token: str) -> FastAPI:
    if not token:
        raise RuntimeError("DISTRESS_API_TOKEN must be set")

    @contextmanager
    def connect():
        db = sqlite3.connect(database, timeout=2)
        db.row_factory = sqlite3.Row
        try:
            # Serialize writes, including read-modify-write and duplicate finalization.
            db.execute("BEGIN IMMEDIATE")
            with db:
                yield db
        finally:
            db.close()

    @asynccontextmanager
    async def lifespan(_):
        database.parent.mkdir(parents=True, exist_ok=True)
        with connect() as db:
            db.execute("""CREATE TABLE IF NOT EXISTS servers (
                id TEXT PRIMARY KEY, balance REAL NOT NULL,
                selected_planet TEXT, votes TEXT NOT NULL DEFAULT '{}')""")
            db.execute("""CREATE TABLE IF NOT EXISTS rounds (
                sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                server_id TEXT NOT NULL, round_id INTEGER NOT NULL,
                planet TEXT NOT NULL, balance_before REAL NOT NULL,
                balance_after REAL, result INTEGER, started_at TEXT NOT NULL,
                finished_at TEXT, UNIQUE(server_id, round_id))""")
        yield

    def authorize(authorization: Annotated[str | None, Header()] = None):
        if authorization is None or not hmac.compare_digest(
            authorization.encode(), f"Bearer {token}".encode()
        ):
            raise HTTPException(401, "Invalid API token")

    api = FastAPI(title="Distress Signal persistence", lifespan=lifespan,
                  dependencies=[Depends(authorize)])

    @api.exception_handler(RequestValidationError)
    async def invalid_request(_, exc):
        errors = jsonable_encoder(
            exc.errors(),
            custom_encoder={float: lambda value: value if math.isfinite(value) else str(value)},
        )
        return JSONResponse(status_code=422, content={"detail": errors})

    @api.exception_handler(sqlite3.OperationalError)
    async def unavailable(_, exc):
        return JSONResponse(status_code=503, content={"detail": "Storage unavailable"})

    def get_server(db, server_id):
        if not server_id or len(server_id) > 200:
            raise HTTPException(422, "Invalid server ID")
        row = db.execute("SELECT * FROM servers WHERE id = ?", (server_id,)).fetchone()
        if row is None:
            raise HTTPException(404, "Load server state first")
        return row

    def history(db, server_id, count):
        rows = db.execute("""SELECT planet FROM rounds WHERE server_id = ?
                             ORDER BY sequence DESC LIMIT ?""", (server_id, count)).fetchall()
        return [row["planet"] for row in reversed(rows)]

    @api.post("/servers/{server_id}/load")
    def load(server_id: str, body: Load):
        if not server_id or len(server_id) > 200:
            raise HTTPException(422, "Invalid server ID")
        with connect() as db:
            db.execute("INSERT OR IGNORE INTO servers(id, balance) VALUES (?, ?)",
                       (server_id, body.initial_marines_per_xeno))
            state = get_server(db, server_id)
            return {"marinesPerXeno": state["balance"],
                    "recentPlanetIds": history(db, server_id, body.recent_planet_count),
                    "carryoverVotes": json.loads(state["votes"]),
                    "selectedPlanetId": state["selected_planet"]}

    @api.post("/servers/{server_id}/history")
    def recent(server_id: str, body: History):
        with connect() as db:
            get_server(db, server_id)
            return history(db, server_id, body.count)

    @api.put("/servers/{server_id}/voting")
    def voting(server_id: str, body: Voting):
        with connect() as db:
            get_server(db, server_id)
            votes = {key: value for key, value in body.carryover_votes.items() if value > 0}
            db.execute("UPDATE servers SET selected_planet = ?, votes = ? WHERE id = ?",
                       (body.selected_planet_id, json.dumps(votes), server_id))
        return {"ok": True}

    @api.put("/servers/{server_id}/balance")
    def balance(server_id: str, body: Balance):
        with connect() as db:
            get_server(db, server_id)
            db.execute("UPDATE servers SET balance = ? WHERE id = ?",
                       (body.marines_per_xeno, server_id))
        return {"ok": True}

    @api.put("/servers/{server_id}/rounds/{round_id}/start")
    def start(server_id: str, round_id: int, body: StartRound):
        if round_id < 0:
            raise HTTPException(422, "Invalid round ID")
        with connect() as db:
            get_server(db, server_id)
            existing = db.execute("SELECT planet FROM rounds WHERE server_id = ? AND round_id = ?",
                                  (server_id, round_id)).fetchone()
            if existing is not None:
                if existing["planet"] != body.planet_id:
                    raise HTTPException(409, "Round already uses another planet")
                # An old retry must not erase a newer vote's selected planet.
                return {"ok": True}
            db.execute("""INSERT INTO rounds(server_id, round_id, planet, balance_before, started_at)
                          VALUES (?, ?, ?, ?, ?)""",
                       (server_id, round_id, body.planet_id, body.marines_per_xeno,
                        datetime.now(timezone.utc).isoformat()))
            db.execute("UPDATE servers SET selected_planet = NULL WHERE id = ?", (server_id,))
        return {"ok": True}

    @api.put("/servers/{server_id}/rounds/{round_id}/finish")
    def finish(server_id: str, round_id: int, body: FinishRound):
        with connect() as db:
            state = get_server(db, server_id)
            row = db.execute("SELECT finished_at FROM rounds WHERE server_id = ? AND round_id = ?",
                             (server_id, round_id)).fetchone()
            if row is None:
                raise HTTPException(404, "Start round first")
            if row["finished_at"] is not None:
                return state["balance"]
            db.execute("""UPDATE rounds SET result = ?, balance_after = ?, finished_at = ?
                          WHERE server_id = ? AND round_id = ?""",
                       (body.result, body.marines_per_xeno, datetime.now(timezone.utc).isoformat(),
                        server_id, round_id))
            db.execute("UPDATE servers SET balance = ? WHERE id = ?",
                       (body.marines_per_xeno, server_id))
        return body.marines_per_xeno

    return api


def app():
    """Uvicorn factory: configuration is read on startup, not on import."""
    return create_app(Path(os.environ.get("DISTRESS_API_DB", "distress-signal.sqlite3")),
                      os.environ.get("DISTRESS_API_TOKEN", ""))
