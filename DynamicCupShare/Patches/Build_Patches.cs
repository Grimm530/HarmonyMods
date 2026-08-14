using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    internal static class Planner_DoBuild_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Planner __instance, Construction.Target target, Construction component, ref BaseEntity __result)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || !plugin.BuildingRestrictionsEnabled)
                return true;

            try
            {
                if (plugin.CanBuild(__instance, component, target) != null)
                {
                    __result = null;
                    return false;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] CanBuild: " + ex.Message); }
            return true;
        }
    }
}
