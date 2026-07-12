using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    [HarmonyPatch(typeof(PlaceMonumentsOffshore), nameof(PlaceMonumentsOffshore.Process))]
    public static class PlaceMonumentsOffshore_Process_Patch
    {
        static void Prefix(PlaceMonumentsOffshore __instance, ref uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisablePlaceMonumentsOffshorePatch)
                return;
            // Control island count if configured (TargetCount is a public field)
            {
                // Check if islands are disabled
                if (!config.IslandsEnabled || config.IslandIntensity == 0)
                {
                    int originalTargetCount = __instance.TargetCount;
                    __instance.TargetCount = 0;
                    UnityEngine.Debug.Log($"[CustomMapGen] Islands disabled - overriding TargetCount from {originalTargetCount} to 0");
                    return;
                }
                
                // Adjust TargetCount based on intensity (0-10 scale)
                // Intensity 7 = default, scale proportionally
                if (config.IslandIntensity > 0 && config.IslandIntensity != 7)
                {
                    int originalTargetCount = __instance.TargetCount;
                    // Scale TargetCount based on intensity (7 = 100%, 10 = ~143%, 1 = ~14%)
                    float intensityMultiplier = config.IslandIntensity / 7f;
                    int newTargetCount = Mathf.RoundToInt(originalTargetCount * intensityMultiplier);
                    __instance.TargetCount = Mathf.Max(0, newTargetCount);
                    UnityEngine.Debug.Log($"[CustomMapGen] Island intensity {config.IslandIntensity}/10 - overriding TargetCount from {originalTargetCount} to {__instance.TargetCount}");
                }
            }
        }
    }
}
