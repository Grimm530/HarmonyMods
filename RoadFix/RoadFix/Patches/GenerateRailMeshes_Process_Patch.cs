using HarmonyLib;
using RoadFix.Bridge;

namespace RoadFix.Patches;

/// <summary>
/// After rail meshes: place bridgerail.map on spans prepared during Rail Terrain.
/// </summary>
[HarmonyPatch(typeof(GenerateRailMeshes), nameof(GenerateRailMeshes.Process))]
public static class GenerateRailMeshes_Process_Patch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!RoadFixConfig.IsEnabled())
            return;
        if (RoadFixConfig.Config?.SpawnCustomBridges != true)
            return;

        BridgeService.PlaceRailBridges();
    }
}
