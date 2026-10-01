using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch GeneratePowerlineLayout.Process to skip if powerlines are disabled
    [HarmonyPatch(typeof(GeneratePowerlineLayout), nameof(GeneratePowerlineLayout.Process))]
    public static class GeneratePowerlineLayout_Process_Patch
    {
        static bool Prefix(GeneratePowerlineLayout __instance, ref uint seed)
        {
            if (CustomMapGen.IsCustomMapGenEnabled())
            {
                var config = CustomMapGen.Instance.GetConfig();
                // Isolate topology/issues: set DisablePowerlineLayoutPatch=true to skip this patch (same effect as Powerlines=false).
                if (config.DisablePowerlineLayoutPatch || !config.Powerlines)
                {
                    var powerlines = TerrainPathAccess.GetPowerlines(TerrainMeta.Path);
                    if (powerlines != null)
                        powerlines.Clear();
                    UnityEngine.Debug.Log("[CustomMapGen] Powerlines disabled - skipping GeneratePowerlineLayout.Process()");
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Destroying the center compound (post-PathLink replace) leaves PowerlineNode refs in TerrainPath.wires.
    /// CreateWires has no null check — scrub destroyed nodes before wire mesh generation.
    /// </summary>
    [HarmonyPatch(typeof(GenerateWireMeshes), nameof(GenerateWireMeshes.Process))]
    public static class GenerateWireMeshes_Process_ScrubDestroyedWires_Patch
    {
        static void Prefix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            int n = TerrainPathAccess.ScrubWires(TerrainMeta.Path);
            if (n > 0)
                UnityEngine.Debug.Log($"[CustomMapGen] Scrubbed {n} destroyed PowerlineNode(s) before CreateWires.");
        }
    }
}
