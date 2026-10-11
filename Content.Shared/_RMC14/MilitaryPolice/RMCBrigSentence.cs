using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.MilitaryPolice;

[Serializable, NetSerializable]
public enum RMCBrigSentenceStatus : byte
{
    Ready,
    Active,
    Paused,
    Served,
    Pardoned,
}

/// <summary>
/// Execution of a particular issued sentence travels with its original report when a prisoner changes cells.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public partial struct RMCBrigSentence
{
    [DataField]
    public RMCBrigSentenceStatus Status;

    [DataField]
    public TimeSpan TimeServed;

    [DataField]
    public string? PardonedBy;

    [DataField]
    public TimeSpan? PardonedAt;

    public readonly string GetDescription()
    {
        if (Status == RMCBrigSentenceStatus.Active)
            return Loc.GetString("rmc-brig-detention-active");
        var text = Loc.GetString("rmc-brig-detention-value",
            ("status", Loc.GetString($"rmc-brig-status-{Status}")),
            ("time", FormatTime(TimeServed)));
        if (PardonedBy != null)
            text += "\n" + Loc.GetString("rmc-brig-pardoned-by", ("officer", PardonedBy),
                ("time", PardonedAt?.ToString(@"hh\:mm\:ss") ?? ""));
        return text;
    }

    public static string FormatTime(TimeSpan time)
    {
        var seconds = (long) Math.Ceiling(Math.Max(0, time.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
