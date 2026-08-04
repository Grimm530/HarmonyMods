using System;
using HarmonyLib;
using UnityEngine;

namespace ServerPanelHarmony.Patches
{
    /// <summary>Oxide OnPlayerConnected - drives the auto-open menu on join.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { ServerPanelHarmonyMod.Instance?.OnPlayerConnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] OnPlayerConnected: " + ex.Message); }
        }
    }

    /// <summary>Oxide OnPlayerDisconnected - clears rate limits and open menu state.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { ServerPanelHarmonyMod.Instance?.OnPlayerDisconnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[ServerPanel] OnPlayerDisconnected: " + ex.Message); }
        }
    }
}
