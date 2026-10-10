using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.MilitaryPolice;

[Serializable, NetSerializable]
public enum RMCMarineLawCategory : byte
{
    Minor,
    Major,
    Capital,
    Optional,
    Precautionary,
    Civilian,
}

/// <summary>
/// A charge from CM-SS13's law catalogue. Optional charges add to the highest ordinary sentence.
/// </summary>
[Prototype("rmcMarineLaw")]
public sealed partial class RMCMarineLawPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public RMCMarineLawCategory Category;

    [DataField(required: true)]
    public float BrigTime;

    [DataField]
    public LocId? SpecialPunishment;

    [DataField]
    public LocId? Conditions;
}
