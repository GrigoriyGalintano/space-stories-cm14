using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.MilitaryPolice;

/// <summary>
/// Status resolved from the worn ID's station record, rather than the wearer's displayed name.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCPoliceStatusComponent : Component
{
    [DataField, AutoNetworkedField]
    public RMCPoliceStatus Status;
}
