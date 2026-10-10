using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server._Stories.DistressSignal;

public sealed record StoriesDistressSignalRound(int RoundId, string PlanetId, float BalanceBefore,
    float? BalanceAfter = null, int? Result = null);

public sealed record StoriesDistressSignalState(float MarinesPerXeno,
    Dictionary<string, int> CarryoverVotes, string? SelectedPlanetId, List<StoriesDistressSignalRound> Rounds);

public sealed class StoriesDistressSignalStore
{
    private sealed record Envelope(int Version, string Data, string Sha256);

    private static readonly ResPath Directory = new("/distress-signal");
    private readonly IWritableDirProvider _data;
    private long _generation;
    private bool _dirty;

    public StoriesDistressSignalState State { get; private set; }
    public bool HasSnapshot { get; private set; }
    public List<string> RecoveryErrors { get; } = new();

    public StoriesDistressSignalStore(IWritableDirProvider data, float initialBalance)
    {
        _data = data;
        State = new(initialBalance, new(), null, new());
        try
        {
            _data.CreateDir(Directory);
            foreach (var file in Checkpoints())
            {
                try
                {
                    var generation = long.Parse(file[11..^5], CultureInfo.InvariantCulture);
                    _generation = Math.Max(_generation, generation);
                    using var stream = _data.Open(Directory / file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var envelope = JsonSerializer.Deserialize<Envelope>(stream)
                        ?? throw new InvalidDataException("Empty checkpoint");
                    if (envelope.Version != 1 || Hash(envelope.Data) != envelope.Sha256)
                        throw new InvalidDataException("Invalid checkpoint version or checksum");
                    var state = JsonSerializer.Deserialize<StoriesDistressSignalState>(envelope.Data)
                        ?? throw new InvalidDataException("Empty state");
                    Validate(state);
                    State = state;
                    HasSnapshot = true;
                    break;
                }
                catch (Exception e)
                {
                    RecoveryErrors.Add($"{file}: {e.Message}");
                    try { _data.Rename(Directory / file, Directory / (file + ".corrupt-" + Guid.NewGuid().ToString("N"))); }
                    catch (Exception renameError) { RecoveryErrors.Add(renameError.Message); }
                }
            }
        }
        catch (Exception e) { RecoveryErrors.Add(e.Message); }
        _dirty = !HasSnapshot;
    }

    public void SetBalance(float value) => Update(State with { MarinesPerXeno = value });

    public void SetVotingState(string? selectedPlanetId, Dictionary<string, int> votes) =>
        Update(State with { SelectedPlanetId = selectedPlanetId, CarryoverVotes = new(votes) });

    public void StartRound(int roundId, string planetId, float balance)
    {
        var existing = State.Rounds.Find(r => r.RoundId == roundId);
        if (existing != null)
        {
            if (existing.PlanetId != planetId)
                throw new InvalidDataException("Round ID already uses another planet");
            return;
        }

        var rounds = State.Rounds.ToList();
        rounds.Add(new(roundId, planetId, balance));
        Update(State with { Rounds = rounds, SelectedPlanetId = null });
    }

    public void FinishRound(int roundId, int result, float balance)
    {
        var index = State.Rounds.FindIndex(r => r.RoundId == roundId);
        if (index < 0)
            throw new InvalidDataException("Round finish has no matching start");
        if (State.Rounds[index].Result != null)
            return;

        var rounds = State.Rounds.ToList();
        rounds[index] = rounds[index] with { Result = result, BalanceAfter = balance };
        Update(State with { Rounds = rounds, MarinesPerXeno = balance });
    }

    private void Update(StoriesDistressSignalState state)
    {
        Validate(state);
        State = state;
        _dirty = true;
    }

    public void Flush()
    {
        if (!_dirty)
            return;

        _data.CreateDir(Directory);
        var name = "checkpoint-" + checked(++_generation).ToString("D20", CultureInfo.InvariantCulture);
        var temporary = Directory / "checkpoint.tmp";
        var payload = JsonSerializer.Serialize(State);
        using (var stream = _data.Open(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new Envelope(1, payload, Hash(payload)));
            if (stream is FileStream file)
                file.Flush(flushToDisk: true);
            else
                stream.Flush();
        }

        // Publish a complete snapshot and retain the previous one for recovery.
        _data.Rename(temporary, Directory / (name + ".json"));
        _dirty = false;
        HasSnapshot = true;
        foreach (var old in Checkpoints().Skip(2))
        {
            try { _data.Delete(Directory / old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string[] Checkpoints() => _data.DirectoryEntries(Directory)
        .Where(f => f.StartsWith("checkpoint-", StringComparison.Ordinal) && f.EndsWith(".json", StringComparison.Ordinal))
        .OrderDescending(StringComparer.Ordinal).ToArray();

    private static void Validate(StoriesDistressSignalState state)
    {
        if (!float.IsFinite(state.MarinesPerXeno) || state.MarinesPerXeno <= 0 ||
            state.CarryoverVotes == null || state.Rounds == null ||
            state.SelectedPlanetId != null && string.IsNullOrWhiteSpace(state.SelectedPlanetId) ||
            state.CarryoverVotes.Any(v => string.IsNullOrWhiteSpace(v.Key) || v.Value < 0) ||
            state.Rounds.Select(r => r.RoundId).Distinct().Count() != state.Rounds.Count ||
            state.Rounds.Any(r => r.RoundId < 0 || string.IsNullOrWhiteSpace(r.PlanetId) ||
                !float.IsFinite(r.BalanceBefore) || r.BalanceBefore <= 0 ||
                r.BalanceAfter is { } balance && (!float.IsFinite(balance) || balance <= 0) ||
                (r.Result == null) != (r.BalanceAfter == null) || r.Result is < 1 or > 6))
            throw new InvalidDataException("Invalid Distress Signal state");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
