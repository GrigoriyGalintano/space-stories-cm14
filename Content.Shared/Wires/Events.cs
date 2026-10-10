using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Wires;

/// <summary>
/// Raised on the device after a user successfully cuts an intact wire.
/// </summary>
[ByRefEvent]
public readonly record struct WireCutEvent(EntityUid User);

/// <summary>
/// Raised on the device after a user successfully mends a cut wire.
/// </summary>
[ByRefEvent]
public readonly record struct WireMendedEvent(EntityUid User);

[Serializable, NetSerializable]
public sealed partial class WireDoAfterEvent : DoAfterEvent
{
    [DataField("action", required: true)]
    public WiresAction Action;

    [DataField("id", required: true)]
    public int Id;

    private WireDoAfterEvent()
    {
    }

    public WireDoAfterEvent(WiresAction action, int id)
    {
        Action = action;
        Id = id;
    }

    public override DoAfterEvent Clone() => this;
}
