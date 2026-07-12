using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch WorldConfig.MergeScriptableConfig to prevent it from overriding our settings
    [HarmonyPatch(typeof(WorldConfig), "MergeScriptableConfig")]
    public static class WorldConfig_MergeScriptableConfig_Patch
    {
        static void Postfix(WorldConfig __instance, ScriptableWorldConfig config)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            WorldConfigPatches_Apply.ApplyConfigToWorldConfig(__instance);
        }
    }
}
