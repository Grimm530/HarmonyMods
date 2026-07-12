using HarmonyLib;

namespace CustomMapGen.Patches
{
    [HarmonyPatch(typeof(GenerateRiverLayout), nameof(GenerateRiverLayout.Process))]
    public static class GenerateRiverLayout_Process_Patch
    {
        static bool Prefix(GenerateRiverLayout __instance, ref uint seed)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                // Isolate topology/issues: set DisableRiverLayoutPatch=true to skip our patch (vanilla river behavior).
                if (config.DisableRiverLayoutPatch)
                    return true;
                if (config.RemoveRivers)
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Rivers disabled - skipping GenerateRiverLayout.Process()");
                    return false;
                }
            }
            return true;
        }
    }
}
