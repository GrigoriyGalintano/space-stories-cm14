using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.MilitaryPolice;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class RMCIncidentReport
{
    public const float PermanentSentence = 60;
    public const int MaxEntries = 32;

    [DataField]
    public string Suspect = string.Empty;

    [DataField]
    public uint? RecordId;

    [DataField]
    public NetEntity? RecordStation;

    [DataField]
    public string Summary = string.Empty;

    [DataField]
    public List<ProtoId<RMCMarineLawPrototype>> Charges = new();

    [DataField]
    public List<RMCIncidentEntry> Witnesses = new();

    [DataField]
    public List<RMCIncidentEntry> Evidence = new();

    public float CalculateSentence(IPrototypeManager prototypes)
    {
        var highest = 0f;
        var optional = 0f;
        foreach (var charge in Charges)
        {
            if (!prototypes.TryIndex(charge, out var law))
                continue;

            if (law.Category == RMCMarineLawCategory.Optional)
                optional += law.BrigTime;
            else
                highest = Math.Max(highest, law.BrigTime);
        }

        return Math.Min(PermanentSentence, highest + optional);
    }

    public RMCIncidentReport Clone()
    {
        return new RMCIncidentReport
        {
            Suspect = Suspect,
            RecordId = RecordId,
            RecordStation = RecordStation,
            Summary = Summary,
            Charges = new(Charges),
            Witnesses = Witnesses.ConvertAll(entry => new RMCIncidentEntry(entry.Name, entry.Notes)),
            Evidence = Evidence.ConvertAll(entry => new RMCIncidentEntry(entry.Name, entry.Notes)),
        };
    }
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class RMCIncidentEntry
{
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Notes = string.Empty;

    public RMCIncidentEntry() { }

    public RMCIncidentEntry(string name, string notes = "")
    {
        Name = name;
        Notes = notes;
    }
}
