using HarmonyLib;
using UnityEngine;

namespace Rustcord.Patches;

/// <summary>Postfix on BasePlayer.PlayerInit - player fully connected.</summary>
[HarmonyPatch(typeof(BasePlayer), "PlayerInit")]
internal class BasePlayer_PlayerInit_Patch
{
    [HarmonyPostfix]
    static void Postfix(BasePlayer __instance)
    {
        if (RustcordMod.Instance == null) return;
        if (__instance == null || !__instance.IsValid()) return;
        var cfg = RustcordConfig.Config;
        if (cfg?.PostSettings?.JoinsQuits != true) return;

        var serverName = cfg?.General?.ServerName ?? "";
        var formatted = RustcordMod.FormatJoin(serverName, __instance.displayName ?? "?");
        RustcordMod.PostToDiscord(formatted, "msg_join");
    }
}
