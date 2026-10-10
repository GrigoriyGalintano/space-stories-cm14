using Content.Shared._RMC14.MilitaryPolice;

namespace Content.Server._RMC14.MilitaryPolice;

[RegisterComponent]
public sealed partial class RMCClueScannerComponent : Component
{
    [DataField]
    public TimeSpan ScanDuration = TimeSpan.FromSeconds(5);

    [DataField]
    public float Range = 2;

    [DataField]
    public List<RMCPoliceClue> Clues = new();

    [DataField]
    public bool Scanning;
}
