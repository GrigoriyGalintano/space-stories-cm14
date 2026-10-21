using Content.Client._RMC14.UserInterface.Crt;
using Content.Shared._RMC14.Dropship;

namespace Content.Client._Stories.Dropship;

internal static class DropshipAutopilotText
{
    public static string Mode(DropshipAutopilotMode mode)
    {
        return Loc.GetString(mode switch
        {
            DropshipAutopilotMode.Cycle => "stories-dropship-remote-ui-cycle",
            DropshipAutopilotMode.RecallOnly => "stories-dropship-remote-ui-recall-only",
            _ => "stories-dropship-mode-disabled",
        });
    }

    public static string Status(DropshipAutopilotStatus status)
    {
        return Loc.GetString(status switch
        {
            DropshipAutopilotStatus.Ready => "stories-dropship-autopilot-status-ready",
            DropshipAutopilotStatus.Waiting => "stories-dropship-autopilot-status-waiting",
            DropshipAutopilotStatus.InFlight => "stories-dropship-autopilot-status-in-flight",
            DropshipAutopilotStatus.Blocked => "stories-dropship-autopilot-status-blocked",
            DropshipAutopilotStatus.Error => "stories-dropship-autopilot-status-error",
            _ => "stories-dropship-autopilot-status-offline",
        });
    }

    public static RMCCrtTone Tone(DropshipAutopilotStatus status)
    {
        return status switch
        {
            DropshipAutopilotStatus.Ready => RMCCrtTone.Good,
            DropshipAutopilotStatus.Waiting or DropshipAutopilotStatus.Blocked => RMCCrtTone.Warning,
            DropshipAutopilotStatus.Error => RMCCrtTone.Danger,
            DropshipAutopilotStatus.Offline => RMCCrtTone.Muted,
            _ => RMCCrtTone.Default,
        };
    }

    public static string Details(string details, int? departInSeconds)
    {
        return departInSeconds is { } seconds
            ? Loc.GetString("stories-dropship-departure-countdown", ("details", details), ("seconds", seconds))
            : details;
    }
}
