using System;
using System.Collections.Generic;
using HarmonyLib;
using Network;

namespace ChatIcons.Patches;

internal static class ChatIconHelper
{
    /// <summary>
    /// When command is chat.add or chat.add2 and args[1] (userId) is 0, replace with configured Steam Avatar User ID.
    /// </summary>
    public static void ApplySteamAvatarUserId(string command, object[] args)
    {
        if (args == null || args.Length < 2) return;
        if (command != "chat.add" && command != "chat.add2") return;

        var cfg = ChatIconsConfig.Config;
        if (cfg == null || cfg.SteamAvatarUserID == 0) return;

        object arg1 = args[1];
        if (arg1 == null) return;

        ulong providedId;
        if (ulong.TryParse(arg1.ToString(), out providedId) && providedId == 0)
            args[1] = cfg.SteamAvatarUserID;
    }
}

[HarmonyPatch(typeof(ConsoleNetwork), nameof(ConsoleNetwork.BroadcastToAllClients))]
internal class ConsoleNetwork_BroadcastToAllClients_Patch
{
    [HarmonyPrefix]
    static void Prefix(string strCommand, object[] args)
    {
        try { ChatIconHelper.ApplySteamAvatarUserId(strCommand, args); }
        catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
    }
}

[HarmonyPatch(typeof(ConsoleNetwork), nameof(ConsoleNetwork.SendClientCommand), new Type[] { typeof(Connection), typeof(string), typeof(object[]) })]
internal class ConsoleNetwork_SendClientCommand_Connection_Patch
{
    [HarmonyPrefix]
    static void Prefix(string strCommand, object[] args)
    {
        try { ChatIconHelper.ApplySteamAvatarUserId(strCommand, args); }
        catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
    }
}

[HarmonyPatch(typeof(ConsoleNetwork), nameof(ConsoleNetwork.SendClientCommand), new Type[] { typeof(List<Connection>), typeof(string), typeof(object[]) })]
internal class ConsoleNetwork_SendClientCommand_List_Patch
{
    [HarmonyPrefix]
    static void Prefix(string strCommand, object[] args)
    {
        try { ChatIconHelper.ApplySteamAvatarUserId(strCommand, args); }
        catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
    }
}
