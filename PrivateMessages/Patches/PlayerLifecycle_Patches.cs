using HarmonyLib;
using UnityEngine;

namespace PrivateMessagesHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = PrivateMessagesMod.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[PrivateMessages] OnDisconnected: " + ex.Message); }
        }
    }
}
