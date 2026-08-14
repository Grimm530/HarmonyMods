using HarmonyLib;
using UnityEngine;
using HMPlugin = Oxide.Plugins.HitMarkers;

namespace HitMarkersHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class PlayerLifecycle_Patches
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { HMPlugin.GetModInstance()?.OnPlayerDisconnectedHarmony(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[HitMarkers] OnDisconnected: " + ex.Message); }
        }
    }
}
