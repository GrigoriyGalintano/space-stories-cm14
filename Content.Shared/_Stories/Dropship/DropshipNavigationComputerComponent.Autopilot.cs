using Content.Shared.Doors.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;

// ReSharper disable CheckNamespace
namespace Content.Shared._RMC14.Dropship;

public sealed partial class DropshipNavigationComputerComponent
{
    [DataField, AutoNetworkedField]
    public bool ShowAutopilotControls = true;

    [AutoNetworkedField]
    public NetEntity? FlyBy;

    [AutoNetworkedField]
    public List<Destination> Destinations = new();

    [AutoNetworkedField]
    public Dictionary<DoorLocation, bool> DoorLockStatus = new();

    [AutoNetworkedField]
    public FTLState? TravelState;

    [AutoNetworkedField]
    public StartEndTime TravelTime;

    [AutoNetworkedField]
    public string TravelDestination = string.Empty;

    [AutoNetworkedField]
    public string TravelDeparture = string.Empty;

    [AutoNetworkedField]
    public DropshipNavigationAutopilotStatus? Autopilot;
}
