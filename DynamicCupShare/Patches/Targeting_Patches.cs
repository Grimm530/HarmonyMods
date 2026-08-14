using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(TriggerBase), nameof(TriggerBase.OnEntityEnter))]
    internal static class TriggerBase_OnEntityEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(TriggerBase __instance, BaseEntity ent)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance is not TargetTrigger targetTrigger || ent is not BasePlayer player)
                return true;

            try
            {
                if (plugin.OnEntityEnter(targetTrigger, player) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnEntityEnter: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(SamSite), nameof(SamSite.TargetScan))]
    internal static class SamSite_TargetScan_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(SamSite __instance)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null || !plugin.SamSitesEnabled)
                return;

            try
            {
                if (__instance.currentTarget != null
                    && plugin.OnSamSiteTarget(__instance, __instance.currentTarget) != null)
                {
                    __instance.ClearTarget();
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnSamSiteTarget: " + ex.Message); }
        }
    }
}
