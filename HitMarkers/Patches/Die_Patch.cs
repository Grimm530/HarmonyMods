using HarmonyLib;
using UnityEngine;
using HMPlugin = Oxide.Plugins.HitMarkers;

namespace HitMarkersHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Die_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null) return;
            try { HMPlugin.GetModInstance()?.OnEntityDied(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[HitMarkers] Die postfix: " + ex.Message); }
        }
    }
}
