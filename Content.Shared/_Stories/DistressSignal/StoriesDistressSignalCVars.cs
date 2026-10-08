using Robust.Shared.Configuration;

namespace Content.Shared._Stories.DistressSignal;

[CVarDefs]
public sealed class StoriesDistressSignalCVars
{
    /// <summary>FastAPI base URL. Empty disables persistence. Restart after changing these settings.</summary>
    public static readonly CVarDef<string> ApiUrl =
        CVarDef.Create("stories.distress_api_url", "", CVar.SERVERONLY);

    public static readonly CVarDef<string> ApiToken =
        CVarDef.Create("stories.distress_api_token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>Stable, unique key for this game server, independent of its display name.</summary>
    public static readonly CVarDef<string> ServerId =
        CVarDef.Create("stories.distress_server_id", "", CVar.SERVERONLY);
}
