using System;
using HarmonyLib;
using RoadFix.Bridge;
using UnityEngine;

namespace RoadFix.Patches;

/// <summary>
/// After vanilla road terrain (paths + height fill untouched): re-apply river height
/// so raised road fill through rivers is cut back — then place bridges later.
/// </summary>
[HarmonyPatch(typeof(GenerateRoadTerrain), nameof(GenerateRoadTerrain.Process))]
public static class GenerateRoadTerrain_Process_Patch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            if (!RoadFixConfig.IsEnabled())
                return;

            var cfg = RoadFixConfig.Config;
            if (cfg == null)
                return;

            RiverProximity.Invalidate();
            RiverProximity.EnsureCache();

            if (cfg.SpawnCustomBridges)
            {
                BridgeService.PrepareRoadCrossings();
                // Rails prepared earlier; refresh if empty so rail crossings get local carve too.
                if (BridgeService.RailCrossingCount == 0)
                    BridgeService.PrepareRailCrossings();
            }

            // Cut road/rail fill at crossings only — NOT full-river OuterFade≈64.
            if (cfg.ReapplyRiverHeightAfterRoads)
                RiverTerrainReapply.Reapply();

            if (cfg.DebugLogging)
            {
                Debug.Log(
                    $"[RoadFix] After Road Terrain: localRiverCarve={cfg.ReapplyRiverHeightAfterRoads} " +
                    $"roadCrossings={BridgeService.RoadCrossingCount} " +
                    $"railCrossings={BridgeService.RailCrossingCount} (paths untouched)");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoadFix] GenerateRoadTerrain postfix failed. {ex}");
        }
    }
}
