// ReSharper disable CheckNamespace
namespace Content.Shared._RMC14.Dropship;

public sealed partial class DropshipDestinationComponent
{
    [DataField, AutoNetworkedField]
    public DropshipDestinationKind Kind;
}

public enum DropshipDestinationKind
{
    Auto,
    Hangar,
    LandingZone,
}
