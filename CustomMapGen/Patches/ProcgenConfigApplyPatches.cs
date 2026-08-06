using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Ensures CustomMapGen config is applied to World.Config at the start of procedural generation,
    /// so BlockedPrefabs, AboveGroundRails, etc. take effect even if config was overwritten earlier.
    /// </summary>
    [HarmonyPatch(typeof(WorldSetup), nameof(WorldSetup.InitCoroutine))]
    [HarmonyPriority(HarmonyLib.Priority.First)]
    public static class WorldSetup_InitCoroutine_ConfigReset_Patch
    {
        public static bool ConfigAppliedThisGen;

        static void Prefix()
        {
            // Detect load vs generate: if procedural map file already exists, we are loading (not generating). Stay dormant.
            CustomMapGen.UpdateIsLoadingExistingMap(World.SaveFolderName, World.MapFileName);
            ConfigAppliedThisGen = false;
            PlaceMonumentsCompound_Patch.ClearDeferredList();
            DeferredOutpostSpawn.Clear();
            World_AddPrefab_Patch.ResetLiveOutpostSwapState();
            GenerateErosion_ShoreFlatten_Patch.ResetInvocationCount();
        }
    }

    /// <summary>Apply config at the very first procedural component (ProcessProceduralObjects runs first in typical order).</summary>
    [HarmonyPatch(typeof(ProcessProceduralObjects), nameof(ProcessProceduralObjects.Process))]
    public static class ProcessProceduralObjects_Process_ConfigApply_Patch
    {
        static void Prefix()
        {
            if (CustomMapGen.IsLoadingExistingMap)
                return;
            if (WorldSetup_InitCoroutine_ConfigReset_Patch.ConfigAppliedThisGen)
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled() || World.Config == null)
                return;
            if (CustomMapGen.Instance?.GetConfig()?.DisableProcgenConfigApplyPatch == true)
                return;
            WorldConfigPatches_Apply.ApplyConfigToWorldConfig(World.Config);
            WorldSetup_InitCoroutine_ConfigReset_Patch.ConfigAppliedThisGen = true;
        }
    }

    [HarmonyPatch(typeof(GenerateHeight), nameof(GenerateHeight.Process))]
    public static class GenerateHeight_Process_ConfigApply_Patch
    {
        static void Prefix()
        {
            if (CustomMapGen.IsLoadingExistingMap)
                return;
            if (WorldSetup_InitCoroutine_ConfigReset_Patch.ConfigAppliedThisGen)
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled() || World.Config == null)
                return;
            if (CustomMapGen.Instance?.GetConfig()?.DisableProcgenConfigApplyPatch == true)
                return;
            WorldConfigPatches_Apply.ApplyConfigToWorldConfig(World.Config);
            WorldSetup_InitCoroutine_ConfigReset_Patch.ConfigAppliedThisGen = true;
        }
    }
}
