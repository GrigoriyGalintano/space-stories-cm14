using Content.Shared.StationRecords;

namespace Content.Server._RMC14.MilitaryPolice;

/// <summary>
/// The marine's enlistment record, used by police systems independently of their currently worn ID.
/// </summary>
[RegisterComponent]
public sealed partial class RMCPoliceIdentityComponent : Component
{
    [DataField]
    public StationRecordKey Key;
}
