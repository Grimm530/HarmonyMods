using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnPlayerDisconnected: " + ex.Message); }
        }
    }
}
