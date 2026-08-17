using System;
using HarmonyLib;
using Network;

namespace ChatIcons.Patches;

/// <summary>
/// server.motd is a ReplicatedVar. The client draws it locally with the default Rust gear icon
/// (userid 0) — that path never goes through chat.add, so ChatIcons cannot retarget it.
/// Hide the replicated string from clients and send the same text as chat.add with the Steam avatar.
/// </summary>
internal static class MotdIconHelper
{
    public const string MotdFullName = "server.motd";

    static int _suppressDepth;

    public static bool ShouldReplaceMotdIcon()
    {
        var cfg = ChatIconsConfig.Config;
        return cfg != null && cfg.ReplaceMotdIcon && cfg.SteamAvatarUserID != 0;
    }

    public static bool ShouldSuppressReplicatedMotd()
    {
        return ShouldReplaceMotdIcon() && !string.IsNullOrEmpty(ConVar.Server.motd);
    }

    public static void BeginSuppress(out string previous)
    {
        previous = null;
        if (!ShouldSuppressReplicatedMotd())
            return;

        previous = ConVar.Server.motd;
        _suppressDepth++;
        ConVar.Server.motd = string.Empty;
    }

    public static void EndSuppress(string previous)
    {
        if (previous == null)
            return;
        ConVar.Server.motd = previous;
        if (_suppressDepth > 0)
            _suppressDepth--;
    }

    public static bool ShouldBroadcastAfterReplicate()
    {
        return _suppressDepth == 0;
    }

    public static bool FilterIncludesMotd(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;
        return MotdFullName.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
    }

    public static void SendMotdWithIcon(BasePlayer player)
    {
        if (player == null || player.net?.connection == null)
            return;
        if (!ShouldReplaceMotdIcon())
            return;

        string motd = ConVar.Server.motd;
        if (string.IsNullOrEmpty(motd))
            return;

        ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, ChatIconsConfig.Config.SteamAvatarUserID, motd);
    }

    public static void BroadcastMotdWithIcon()
    {
        if (!ShouldReplaceMotdIcon())
            return;

        string motd = ConVar.Server.motd;
        if (string.IsNullOrEmpty(motd))
            return;

        ConsoleNetwork.BroadcastToAllClients("chat.add", 2, ChatIconsConfig.Config.SteamAvatarUserID, motd);
    }
}

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.SendReplicatedVars), new Type[] { typeof(Connection) })]
internal class ServerMgr_SendReplicatedVars_Connection_Patch
{
    [HarmonyPrefix]
    static void Prefix(out string __state)
    {
        __state = null;
        try { MotdIconHelper.BeginSuppress(out __state); }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            MotdIconHelper.EndSuppress(__state);
            __state = null;
        }
    }

    [HarmonyPostfix]
    static void Postfix(string __state)
    {
        MotdIconHelper.EndSuppress(__state);
    }
}

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.SendReplicatedVars), new Type[] { typeof(string) })]
internal class ServerMgr_SendReplicatedVars_Filter_Patch
{
    [HarmonyPrefix]
    static void Prefix(string filter, out string __state)
    {
        __state = null;
        if (!MotdIconHelper.FilterIncludesMotd(filter))
            return;
        try { MotdIconHelper.BeginSuppress(out __state); }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            MotdIconHelper.EndSuppress(__state);
            __state = null;
        }
    }

    [HarmonyPostfix]
    static void Postfix(string __state)
    {
        MotdIconHelper.EndSuppress(__state);
    }
}

[HarmonyPatch(typeof(ServerMgr), "OnReplicatedVarChanged")]
internal class ServerMgr_OnReplicatedVarChanged_Patch
{
    [HarmonyPrefix]
    static void Prefix(string fullName, ref string value, out bool __state)
    {
        __state = false;
        if (!string.Equals(fullName, MotdIconHelper.MotdFullName, StringComparison.OrdinalIgnoreCase))
            return;
        if (!MotdIconHelper.ShouldReplaceMotdIcon())
            return;

        value = string.Empty;
        __state = MotdIconHelper.ShouldBroadcastAfterReplicate();
    }

    [HarmonyPostfix]
    static void Postfix(bool __state)
    {
        if (!__state)
            return;
        try { MotdIconHelper.BroadcastMotdWithIcon(); }
        catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
    }
}

[HarmonyPatch(typeof(BasePlayer), "EnterGame")]
internal class BasePlayer_EnterGame_Motd_Patch
{
    [HarmonyPostfix]
    static void Postfix(BasePlayer __instance)
    {
        try { MotdIconHelper.SendMotdWithIcon(__instance); }
        catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
    }
}
