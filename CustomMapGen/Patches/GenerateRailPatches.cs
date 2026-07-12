using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// When "Wanted": forces AboveGroundRails and MinWorldSize so rails run on any map size.
    /// When "NotWanted": sets MinWorldSize = int.MaxValue so the rail ring component skips.
    /// </summary>
    [HarmonyPatch(typeof(GenerateRailRing), nameof(GenerateRailRing.Process))]
    public static class GenerateRailRing_Process_Patch
    {
        static void Prefix(GenerateRailRing __instance)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || World.Config == null || __instance == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.GenerateAboveGroundTrainTracks == "NotWanted")
            {
                __instance.MinWorldSize = int.MaxValue;
                return;
            }
            if (config.GenerateAboveGroundTrainTracks != "Wanted")
                return;
            World.Config.AboveGroundRails = true;
            // Allow rails on any map size: game skips when World.Size < MinWorldSize (e.g. 3500/4000)
            if (World.Size < (uint)__instance.MinWorldSize)
                __instance.MinWorldSize = (int)World.Size;
        }
    }

    [HarmonyPatch(typeof(GenerateRailLayout), nameof(GenerateRailLayout.Process))]
    public static class GenerateRailLayout_Process_Patch
    {
        static void Prefix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || World.Config == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisableRailPatch)
                return;
            if (config.GenerateAboveGroundTrainTracks == "Wanted")
                World.Config.AboveGroundRails = true;
        }
    }
}
