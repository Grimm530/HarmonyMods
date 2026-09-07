// OnEntityDeath — Die patch only. Hurt is handled by 0GrimmCore unified dispatcher.
using HarmonyLib;
using UnityEngine;
using TPVE = Harmony.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            try { TPVE.Dispatch_OnEntityDeath(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntityDeath: " + ex.Message); }
        }
    }
}
