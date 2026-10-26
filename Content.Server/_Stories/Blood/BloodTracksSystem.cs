using System.Numerics;
using Content.Server.Decals;
using Content.Server.Spawners.Components;
using Content.Shared._RMC14.Chemistry.Reagent;
using Content.Shared._Stories.Blood;
using Content.Shared._Stories.SCCVars;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Decals;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Fluids.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Standing;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Stories.Blood;

public sealed class BloodTracksSystem : EntitySystem
{
    private static readonly ProtoId<DecalPrototype> BareLeftDecal = "STDecalBloodFootprintBareLeft";
    private static readonly ProtoId<DecalPrototype> BareRightDecal = "STDecalBloodFootprintBareRight";
    private static readonly ProtoId<DecalPrototype> ShoesDecal = "STDecalBloodFootprintShoes";

    private static readonly ProtoId<DecalPrototype>[] DragDecals =
    [
        "STDecalBloodDrag1",
        "STDecalBloodDrag2",
        "STDecalBloodDrag3",
        "STDecalBloodDrag4",
        "STDecalBloodDrag5",
    ];

    private static readonly Angle TrackRotationOffset = Angle.FromDegrees(180);
    private static readonly Vector2 DecalCenterOffset = new(0.5f, 0.5f);

    private static readonly Dictionary<EntProtoId, Color> SplatterSpawners = new()
    {
        ["RMCDecalSpawnerBloodSplatters"] = Color.FromHex("#980002"),
        ["RMCDecalSpawnerXenoSplatters"] = Color.FromHex("#BED700"),
        ["RMCDecalSpawnerAcidBloodSplash"] = Color.FromHex("#BED700"),
    };

    [Dependency] private readonly BloodStainSystem _bloodStain = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly RMCReagentSystem _reagent = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<DecalGridComponent> _decalGridQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<PuddleComponent> _puddleQuery;

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, WetSplatter>> _wetSplatters = new();
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, Queue<TileDecal>>> _trackDecals = new();
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, Queue<TileDecal>>> _splatterDecals = new();
    private readonly HashSet<ProtoId<DecalPrototype>> _spawnerBloodDecals = new();
    private readonly List<Vector2i> _emptyTiles = new();
    private readonly List<EntityUid> _emptyGrids = new();
    private readonly List<(ReagentId Reagent, FixedPoint2 Quantity)> _puddleBlood = new();
    private readonly HashSet<Entity<BloodyComponent>> _nearbyBloody = new();

    private TimeSpan _splatterDryTime;
    private int _wetSplatterCharges;
    private TimeSpan _wetPruneInterval;
    private float _splatterStainRange;
    private int _maxTracksPerTile;
    private int _maxSplattersPerTile;
    private TimeSpan _nextWetPrune;

    private record struct WetSplatter(Color Color, TimeSpan DriesAt, int Charges);

    private record struct TileDecal(uint Id, Vector2i Chunk);

    public override void Initialize()
    {
        base.Initialize();

        _decalGridQuery = GetEntityQuery<DecalGridComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _puddleQuery = GetEntityQuery<PuddleComponent>();

        SubscribeLocalEvent<BloodyComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<RandomDecalSpawnerComponent, ComponentStartup>(OnDecalSpawnerStartup);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        Subs.CVar(_config, SCCVars.BloodSplatterDryTime, v => _splatterDryTime = TimeSpan.FromSeconds(v), true);
        Subs.CVar(_config, SCCVars.BloodWetSplatterCharges, v => _wetSplatterCharges = v, true);
        Subs.CVar(_config, SCCVars.BloodWetPruneInterval, v => _wetPruneInterval = TimeSpan.FromSeconds(v), true);
        Subs.CVar(_config, SCCVars.BloodSplatterStainRange, v => _splatterStainRange = v, true);
        Subs.CVar(_config, SCCVars.BloodMaxTracksPerTile, v => _maxTracksPerTile = v, true);
        Subs.CVar(_config, SCCVars.BloodMaxFloorSplattersPerTile, v => _maxSplattersPerTile = v, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextWetPrune)
            return;

        _nextWetPrune = now + _wetPruneInterval;
        PruneWetSplatters(now);
        PruneTileDecals(_trackDecals);
        PruneTileDecals(_splatterDecals);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _wetSplatters.Clear();
        _trackDecals.Clear();
        _splatterDecals.Clear();
    }

    private void OnDecalSpawnerStartup(Entity<RandomDecalSpawnerComponent> ent, ref ComponentStartup args)
    {
        if (MetaData(ent).EntityPrototype?.ID is not { } protoId ||
            !SplatterSpawners.TryGetValue(protoId, out var color))
        {
            return;
        }

        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid ||
            !_map.IsInitialized(xform.MapUid) ||
            !_gridQuery.TryComp(gridUid, out var grid))
        {
            return;
        }

        foreach (var decal in ent.Comp.Decals)
        {
            _spawnerBloodDecals.Add(decal);
        }

        var tiles = GetWetTiles(gridUid);
        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var radius = (int) MathF.Ceiling(ent.Comp.Radius);
        var driesAt = _timing.CurTime + _splatterDryTime;
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                tiles[center + new Vector2i(x, y)] = new WetSplatter(color, driesAt, _wetSplatterCharges);
            }
        }

        _nearbyBloody.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(ent, xform), _splatterStainRange, _nearbyBloody);
        foreach (var bloody in _nearbyBloody)
        {
            _bloodStain.StainWearer(bloody.AsNullable(), BloodStainParts.Body, color);
        }
    }

    public bool TryAddSplatter(ProtoId<DecalPrototype> decal, EntityUid gridUid, MapGridComponent grid, Vector2 center, Color color, Angle rotation)
    {
        var tile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, center));
        if (!TryAddTileDecal(_splatterDecals, _maxSplattersPerTile, false, decal, gridUid, tile, center, color, rotation) &&
            !HasBloodDecal(gridUid, tile))
        {
            return false;
        }

        GetWetTiles(gridUid)[tile] = new WetSplatter(color, _timing.CurTime + _splatterDryTime, _wetSplatterCharges);
        return true;
    }

    private bool TryAddTileDecal(
        Dictionary<EntityUid, Dictionary<Vector2i, Queue<TileDecal>>> registry,
        int max,
        bool replaceOldest,
        ProtoId<DecalPrototype> decal,
        EntityUid gridUid,
        Vector2i tile,
        Vector2 center,
        Color color,
        Angle rotation)
    {
        if (max <= 0)
            return false;

        if (!registry.TryGetValue(gridUid, out var tiles))
        {
            tiles = new Dictionary<Vector2i, Queue<TileDecal>>();
            registry[gridUid] = tiles;
        }

        if (!tiles.TryGetValue(tile, out var queue))
        {
            queue = new Queue<TileDecal>();
            tiles[tile] = queue;
        }

        PruneQueue(gridUid, queue);
        if (queue.Count >= max && !replaceOldest)
            return false;

        while (queue.Count >= max)
        {
            _decals.RemoveDecal(gridUid, queue.Dequeue().Id);
        }

        var position = center - DecalCenterOffset;
        if (!_decals.TryAddDecal(decal, new EntityCoordinates(gridUid, position), out var id, color, rotation, cleanable: true))
            return false;

        queue.Enqueue(new TileDecal(id, SharedDecalSystem.GetChunkIndices(position)));
        return true;
    }

    private bool DecalExists(EntityUid gridUid, TileDecal decal)
    {
        return _decalGridQuery.TryComp(gridUid, out var decalGrid) &&
               decalGrid.ChunkCollection.ChunkCollection.TryGetValue(decal.Chunk, out var chunk) &&
               chunk.Decals.ContainsKey(decal.Id);
    }

    private void PruneQueue(EntityUid gridUid, Queue<TileDecal> queue)
    {
        for (var i = queue.Count; i > 0; i--)
        {
            var decal = queue.Dequeue();
            if (DecalExists(gridUid, decal))
                queue.Enqueue(decal);
        }
    }

    private void PruneTileDecals(Dictionary<EntityUid, Dictionary<Vector2i, Queue<TileDecal>>> registry)
    {
        _emptyGrids.Clear();
        foreach (var (grid, tiles) in registry)
        {
            if (TerminatingOrDeleted(grid))
            {
                _emptyGrids.Add(grid);
                continue;
            }

            _emptyTiles.Clear();
            foreach (var (tile, queue) in tiles)
            {
                PruneQueue(grid, queue);
                if (queue.Count == 0)
                    _emptyTiles.Add(tile);
            }

            foreach (var tile in _emptyTiles)
            {
                tiles.Remove(tile);
            }

            if (tiles.Count == 0)
                _emptyGrids.Add(grid);
        }

        foreach (var grid in _emptyGrids)
        {
            registry.Remove(grid);
        }
    }

    private Dictionary<Vector2i, WetSplatter> GetWetTiles(EntityUid gridUid)
    {
        if (!_wetSplatters.TryGetValue(gridUid, out var tiles))
        {
            tiles = new Dictionary<Vector2i, WetSplatter>();
            _wetSplatters[gridUid] = tiles;
        }

        return tiles;
    }

    private void PruneWetSplatters(TimeSpan now)
    {
        _emptyGrids.Clear();
        foreach (var (grid, tiles) in _wetSplatters)
        {
            if (TerminatingOrDeleted(grid))
            {
                _emptyGrids.Add(grid);
                continue;
            }

            _emptyTiles.Clear();
            foreach (var (tile, splatter) in tiles)
            {
                if (splatter.DriesAt <= now)
                    _emptyTiles.Add(tile);
            }

            foreach (var tile in _emptyTiles)
            {
                tiles.Remove(tile);
            }

            if (tiles.Count == 0)
                _emptyGrids.Add(grid);
        }

        foreach (var grid in _emptyGrids)
        {
            _wetSplatters.Remove(grid);
        }
    }

    private bool TryGetWetSplatter(EntityUid gridUid, Vector2i tile, out Color color)
    {
        color = default;
        if (!_wetSplatters.TryGetValue(gridUid, out var tiles) ||
            !tiles.TryGetValue(tile, out var splatter))
        {
            return false;
        }

        if (splatter.DriesAt <= _timing.CurTime || splatter.Charges <= 0 || !HasBloodDecal(gridUid, tile))
        {
            tiles.Remove(tile);
            return false;
        }

        if (splatter.Charges <= 1)
            tiles.Remove(tile);
        else
            tiles[tile] = splatter with { Charges = splatter.Charges - 1 };

        color = splatter.Color;
        return true;
    }

    private bool HasBloodDecal(EntityUid gridUid, Vector2i tile)
    {
        if (_splatterDecals.TryGetValue(gridUid, out var tiles) &&
            tiles.TryGetValue(tile, out var queue))
        {
            PruneQueue(gridUid, queue);
            if (queue.Count > 0)
                return true;
        }

        if (_spawnerBloodDecals.Count == 0)
            return false;

        var bounds = Box2.CenteredAround(new Vector2(tile.X, tile.Y), Vector2.One);
        foreach (var (_, decal) in _decals.GetDecalsIntersecting(gridUid, bounds))
        {
            if (decal.Cleanable && _spawnerBloodDecals.Contains(decal.Id))
                return true;
        }

        return false;
    }

    private void OnMove(Entity<BloodyComponent> ent, ref MoveEvent args)
    {
        if (!ent.Comp.LeavesTracks)
            return;

        var xform = args.Component;
        if (xform.GridUid is not { } gridUid ||
            xform.ParentUid != gridUid ||
            !_gridQuery.TryComp(gridUid, out var grid))
        {
            ent.Comp.TrackGrid = null;
            return;
        }

        var position = xform.LocalPosition;
        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        if (ent.Comp.TrackGrid != gridUid)
        {
            ent.Comp.TrackGrid = gridUid;
            ent.Comp.LastTile = tile;
            ent.Comp.LastTrackPosition = position;
            TryPickUpBlood(ent, gridUid, grid, tile, position);
            return;
        }

        if (tile != ent.Comp.LastTile)
        {
            ent.Comp.LastTile = tile;
            TryPickUpBlood(ent, gridUid, grid, tile, position);
        }

        var delta = position - ent.Comp.LastTrackPosition;
        if (delta.Length() > ent.Comp.MaxTrackJump)
        {
            ent.Comp.LastTrackPosition = position;
            return;
        }

        if (IsDragged(ent))
            TryLeaveDragTrail(ent, gridUid, grid, position, delta);
        else if (CanLeaveFootprints(ent))
            TryLeaveFootprint(ent, gridUid, grid, position, delta);
        else
            ent.Comp.LastTrackPosition = position;
    }

    private void TryPickUpBlood(Entity<BloodyComponent> ent, EntityUid gridUid, MapGridComponent grid, Vector2i tile, Vector2 position)
    {
        if (!TryGetPuddleBlood(ent, gridUid, grid, tile, out var volume, out var color))
        {
            if (!TryGetWetSplatter(gridUid, tile, out color))
                return;

            volume = ent.Comp.WetSplatterVolume;
        }

        var steps = Math.Clamp((int) MathF.Ceiling(volume / ent.Comp.VolumePerStep), 1, ent.Comp.MaxSteps);
        if (_standing.IsDown(ent) || !_mobState.IsAlive(ent))
        {
            ent.Comp.DragSteps = Math.Max(ent.Comp.DragSteps, ent.Comp.MaxDragSteps);
            ent.Comp.TrackColor = color;
            _bloodStain.StainWearer(ent.AsNullable(), BloodStainParts.Body, color);
            return;
        }

        if (ent.Comp.Steps <= 0)
            ent.Comp.LastTrackPosition = position;

        ent.Comp.Steps = Math.Max(ent.Comp.Steps, steps);
        ent.Comp.TrackColor = color;
        _bloodStain.StainWearer(ent.AsNullable(), BloodStainParts.Feet, color);
    }

    private bool TryGetPuddleBlood(Entity<BloodyComponent> ent, EntityUid gridUid, MapGridComponent grid, Vector2i tile, out float volume, out Color color)
    {
        volume = 0f;
        color = default;

        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (!_puddleQuery.TryComp(uid, out var puddle) ||
                !_solution.ResolveSolution(uid.Value, puddle.SolutionName, ref puddle.Solution, out var solution))
            {
                continue;
            }

            _puddleBlood.Clear();
            var puddleVolume = 0f;
            var largest = 0f;
            foreach (var reagent in solution.Contents)
            {
                if (!ent.Comp.BloodReagents.Contains(reagent.Reagent.Prototype))
                    continue;

                _puddleBlood.Add((reagent.Reagent, reagent.Quantity));
                var quantity = reagent.Quantity.Float();
                puddleVolume += quantity;
                if (quantity <= largest ||
                    !_reagent.TryIndex(reagent.Reagent, out var proto))
                {
                    continue;
                }

                largest = quantity;
                color = proto.SubstanceColor;
            }

            if (puddleVolume < ent.Comp.MinPuddleVolume)
                continue;

            var drained = Math.Min(ent.Comp.PuddlePickupVolume, puddleVolume) / puddleVolume;
            foreach (var (reagent, quantity) in _puddleBlood)
            {
                _solution.RemoveReagent(puddle.Solution.Value, reagent, quantity * drained);
            }

            volume = puddleVolume;
            return true;
        }

        return false;
    }

    private bool IsDragged(EntityUid uid)
    {
        return TryComp<PullableComponent>(uid, out var pullable) &&
               pullable.BeingPulled &&
               (_standing.IsDown(uid) || !_mobState.IsAlive(uid));
    }

    private bool CanLeaveFootprints(Entity<BloodyComponent> ent)
    {
        return ent.Comp.Steps > 0 && !_standing.IsDown(ent) && _mobState.IsAlive(ent);
    }

    private void TryLeaveDragTrail(Entity<BloodyComponent> ent, EntityUid gridUid, MapGridComponent grid, Vector2 position, Vector2 delta)
    {
        var bleeding = TryComp<BloodstreamComponent>(ent, out var bloodstream) && bloodstream.BleedAmount > 0;
        if (!bleeding && ent.Comp.DragSteps <= 0)
        {
            ent.Comp.LastTrackPosition = position;
            return;
        }

        if (delta.Length() < ent.Comp.DragDistance)
            return;

        Color color;
        if (bleeding && _bloodStain.TryGetBloodColor(ent, out var bloodColor))
        {
            color = bloodColor.WithAlpha(ent.Comp.BleedingDragAlpha);
        }
        else
        {
            color = ent.Comp.TrackColor.WithAlpha(GetTrackAlpha(ent.Comp, ent.Comp.DragSteps, ent.Comp.MaxDragSteps));
            ent.Comp.DragSteps--;
        }

        var center = ent.Comp.LastTrackPosition + delta / 2;
        AddTrackDecal(_random.Pick(DragDecals), gridUid, grid, center, color, delta.ToWorldAngle());
        ent.Comp.LastTrackPosition = position;
    }

    private void TryLeaveFootprint(Entity<BloodyComponent> ent, EntityUid gridUid, MapGridComponent grid, Vector2 position, Vector2 delta)
    {
        if (delta.Length() < ent.Comp.StepDistance)
            return;

        var direction = Vector2.Normalize(delta);
        var side = new Vector2(-direction.Y, direction.X) * ent.Comp.StepSideOffset;
        if (ent.Comp.RightFoot)
            side = -side;

        var color = ent.Comp.TrackColor.WithAlpha(GetTrackAlpha(ent.Comp, ent.Comp.Steps, ent.Comp.MaxSteps));
        ProtoId<DecalPrototype> decal;
        if (_inventory.TryGetSlotEntity(ent, BloodStainSystem.ShoesSlot, out _))
            decal = ShoesDecal;
        else
            decal = ent.Comp.RightFoot ? BareRightDecal : BareLeftDecal;

        AddTrackDecal(decal, gridUid, grid, position + side, color, delta.ToWorldAngle());

        ent.Comp.RightFoot = !ent.Comp.RightFoot;
        ent.Comp.Steps--;
        ent.Comp.LastTrackPosition = position;
    }

    private void AddTrackDecal(ProtoId<DecalPrototype> decal, EntityUid gridUid, MapGridComponent grid, Vector2 center, Color color, Angle rotation)
    {
        var tile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, center));
        TryAddTileDecal(_trackDecals, _maxTracksPerTile, true, decal, gridUid, tile, center, color, rotation + TrackRotationOffset);
    }

    private static float GetTrackAlpha(BloodyComponent comp, int remaining, int max)
    {
        var fraction = max <= 0 ? 0f : Math.Clamp((float) remaining / max, 0f, 1f);
        return comp.MinTrackAlpha + (comp.MaxTrackAlpha - comp.MinTrackAlpha) * fraction;
    }
}
