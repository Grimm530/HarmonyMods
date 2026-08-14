using HarmonyLib;
using UnityEngine;
using HMPlugin = Oxide.Plugins.HitMarkers;

namespace HitMarkersHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Hurt_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseCombatEntity __instance, HitInfo info, ref float __state)
        {
            __state = __instance != null ? __instance.Health() : 0f;
        }

        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info, float __state)
        {
            if (__instance == null || info == null) return;
            try { HMPlugin.GetModInstance()?.OnHurtObserved(__instance, info, __state); }
            catch (System.Exception ex) { Debug.LogWarning("[HitMarkers] Hurt postfix: " + ex.Message); }
        }
    }
}
