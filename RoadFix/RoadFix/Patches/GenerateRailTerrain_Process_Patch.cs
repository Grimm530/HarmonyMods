using HarmonyLib;
using RoadFix.Bridge;
using UnityEngine;

namespace RoadFix.Patches;

/// <summary>
/// After vanilla rail terrain: record river crossings for bridge place only.
/// Never moves rail path nodes or heightmap.
/// </summary>
[HarmonyPatch(typeof(GenerateRailTerrain), nameof(GenerateRailTerrain.Process))]
public static class GenerateRailTerrain_Process_Patch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!RoadFixConfig.IsEnabled())
            return;
        if (RoadFixConfig.Config?.SpawnCustomBridges != true)
            return;

        BridgeService.PrepareRailCrossings();
        if (RoadFixConfig.Config.DebugLogging)
            Debug.Log($"[RoadFix] After Rail Terrain: railCrossings={BridgeService.RailCrossingCount} (paths untouched)");
    }
}
