using System;
using HarmonyLib;
using RoadFix.Bridge;
using UnityEngine;

namespace RoadFix.Patches;

/// <summary>
/// Before rail meshes: optionally snap crossing rail nodes to a straight deck grade
/// (bridgerail gravel). After meshes: place bridgerail.map.
/// </summary>
[HarmonyPatch(typeof(GenerateRailMeshes), nameof(GenerateRailMeshes.Process))]
public static class GenerateRailMeshes_Process_Patch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            if (!RoadFixConfig.IsEnabled())
                return;

            var cfg = RoadFixConfig.Config;
            if (cfg?.SnapRailNodesToDeck != true)
                return;

            if (BridgeService.RailCrossingCount == 0)
                BridgeService.PrepareRailCrossings();

            int n = 0;
            foreach (BridgeCrossing crossing in BridgeService.GetRailCrossings())
            {
                BridgeTerrain.SnapRailNodesToDeckGrade(crossing);
                n++;
            }

            if (cfg.DebugLogging)
                Debug.Log($"[RoadFix] GenerateRailMeshes prefix: snapped rail nodes on {n} crossing(s)");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoadFix] GenerateRailMeshes prefix failed; vanilla rail meshes continue. {ex}");
        }
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            if (!RoadFixConfig.IsEnabled())
                return;
            if (RoadFixConfig.Config?.SpawnCustomBridges != true)
                return;

            BridgeService.PlaceRailBridges();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoadFix] GenerateRailMeshes postfix failed. {ex}");
        }
    }
}
