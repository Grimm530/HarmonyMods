using HarmonyLib;
using UnityEngine;

namespace AutoCodeLockHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[AutoCodeLock] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[AutoCodeLock] OnPlayerDisconnected: " + ex.Message); }
        }
    }
}
