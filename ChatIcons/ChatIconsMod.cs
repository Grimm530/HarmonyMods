namespace ChatIcons;

/// <summary>
/// Harmony mod: ChatIcons - Set a customizable Steam avatar icon for all non-user chat messages.
/// Loaded by HarmonyLoader from HarmonyMods/. No Oxide plugin required.
/// Config: HarmonyConfig/ChatIcons.json
/// When chat.add or chat.add2 are sent with userId 0 (server/system messages), replaces with Steam Avatar User ID for custom icon.
/// </summary>
internal class ChatIconsMod : IHarmonyModHooks
{
    public static ChatIconsMod Instance { get; private set; }

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        ChatIconsConfig.LoadConfig();
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Instance = null;
    }
}
