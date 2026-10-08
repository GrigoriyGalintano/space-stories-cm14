using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server._Stories.DistressSignal;

public enum StoriesDistressSignalWriteKind
{
    StartRound,
    FinishRound,
    Voting,
    Balance,
}

public sealed record StoriesDistressSignalWrite(
    StoriesDistressSignalWriteKind Kind,
    int RoundId = 0,
    string? PlanetId = null,
    float MarinesPerXeno = 0,
    int Result = 0,
    Dictionary<string, int>? CarryoverVotes = null);

/// <summary>
/// Ordered local journal. Commit each write before applying it to the game; delete only after API acknowledgement.
/// Accessed only on the game thread. A crash after sending but before deleting safely replays the same API operation.
/// </summary>
public sealed class StoriesDistressSignalOutbox
{
    private readonly IWritableDirProvider _data;
    private readonly ResPath _directory;
    private readonly Queue<(ResPath Path, StoriesDistressSignalWrite Write)> _pending = new();
    private long _sequence;

    public int Count => _pending.Count;
    public StoriesDistressSignalWrite Next => _pending.Peek().Write;

    public StoriesDistressSignalOutbox(IWritableDirProvider data, string serverId)
    {
        _data = data;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serverId)));
        _directory = new ResPath($"/distress-signal/{key}");
        _data.CreateDir(_directory);
        foreach (var name in _data.DirectoryEntries(_directory).Order(StringComparer.Ordinal))
        {
            if (!name.EndsWith(".json", StringComparison.Ordinal))
                continue; // An incomplete .tmp file was never accepted by the game.

            var sequence = long.Parse(name[..^5], CultureInfo.InvariantCulture);
            if (sequence <= _sequence)
                throw new InvalidDataException("Invalid Distress Signal journal sequence.");

            var path = _directory / name;
            using var stream = _data.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var write = JsonSerializer.Deserialize<StoriesDistressSignalWrite>(stream)
                ?? throw new InvalidDataException("Empty Distress Signal journal entry.");
            Validate(write);
            _pending.Enqueue((path, write));
            _sequence = sequence;
        }
    }

    public void Enqueue(StoriesDistressSignalWrite write)
    {
        Validate(write);
        var name = checked(++_sequence).ToString("D20", CultureInfo.InvariantCulture);
        var path = _directory / (name + ".json");
        var temporary = _directory / (name + ".tmp");
        using (var stream = _data.Open(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, write);
            if (stream is FileStream file)
                file.Flush(flushToDisk: true);
            else
                stream.Flush(); // Virtual user data used by the engine's tests.
        }

        _data.Rename(temporary, path);
        _pending.Enqueue((path, write));
    }

    public void Acknowledge()
    {
        _data.Delete(_pending.Peek().Path);
        _pending.Dequeue();
    }

    private static void Validate(StoriesDistressSignalWrite write)
    {
        if (!Enum.IsDefined(write.Kind) || write.RoundId < 0 ||
            (write.Kind != StoriesDistressSignalWriteKind.Voting &&
             (!float.IsFinite(write.MarinesPerXeno) || write.MarinesPerXeno <= 0)) ||
            (write.Kind == StoriesDistressSignalWriteKind.StartRound && string.IsNullOrWhiteSpace(write.PlanetId)) ||
            (write.Kind == StoriesDistressSignalWriteKind.FinishRound && write.Result is < 1 or > 6) ||
            (write.Kind == StoriesDistressSignalWriteKind.Voting &&
             (write.CarryoverVotes == null || write.CarryoverVotes.Any(v => string.IsNullOrWhiteSpace(v.Key) || v.Value < 0))))
        {
            throw new InvalidDataException("Invalid Distress Signal journal entry.");
        }
    }
}
