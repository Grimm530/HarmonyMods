using HarmonyLib;
using Network;
using UnityEngine;

namespace Rustcord.Patches;

/// <summary>Postfix on ServerMgr.OnDisconnected - player disconnected.</summary>
[HarmonyPatch(typeof(ServerMgr), "OnDisconnected", new System.Type[] { typeof(string), typeof(Network.Connection) })]
internal class ServerMgr_OnDisconnected_Patch
{
    [HarmonyPostfix]
    static void Postfix(string strReason, Network.Connection connection)
    {
        if (RustcordMod.Instance == null) return;
        if (connection == null) return;
        var cfg = RustcordConfig.Config;
        if (cfg?.PostSettings?.JoinsQuits != true) return;

        var player = connection.player as BasePlayer;
        var name = player?.displayName ?? connection.username ?? connection.userid.ToString();
        var serverName = cfg?.General?.ServerName ?? "";
        var formatted = RustcordMod.FormatQuit(serverName, name, strReason ?? "Unknown");
        RustcordMod.PostToDiscord(formatted, "msg_quit");
    }
}
