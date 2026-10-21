using Robust.Shared.Configuration;

namespace Content.Shared._Stories.SCCVars;

public sealed partial class SCCVars
{
    public static readonly CVarDef<int> DropshipAutopilotDefaultDelaySeconds =
        CVarDef.Create("stories.dropship_autopilot_default_delay_seconds", 30, CVar.SERVER);

    public static readonly CVarDef<int> DropshipAutopilotMinDelaySeconds =
        CVarDef.Create("stories.dropship_autopilot_min_delay_seconds", 10, CVar.SERVER);

    public static readonly CVarDef<int> DropshipAutopilotMaxDelaySeconds =
        CVarDef.Create("stories.dropship_autopilot_max_delay_seconds", 120, CVar.SERVER);

    public static readonly CVarDef<int> DropshipAutopilotRetrySeconds =
        CVarDef.Create("stories.dropship_autopilot_retry_seconds", 10, CVar.SERVER);
}
