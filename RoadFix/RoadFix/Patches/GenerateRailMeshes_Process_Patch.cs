using System;
using System.Collections.Generic;
using HarmonyLib;
using RoadFix.Bridge;
using UnityEngine;

namespace RoadFix.Patches;

/// <summary>
/// Before rail meshes: snap crossing rail nodes to the shared bridge.map deck.
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

            if (World.Cached)
            {
                BridgeService.NoteCachedIdle();
                return;
            }

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
                List<BridgeCrossing> extras = crossing.ExtraSpans;
                if (extras == null)
                    continue;
                BridgeMapPlacer.GetRailDeckGrade(crossing, out float y0, out float y1);
                for (int i = 0; i < extras.Count; i++)
                {
                    if (extras[i].SpanLength < 1f || extras[i].Path?.Path == null)
                        continue;
                    BridgeTerrain.SnapRailNodesToDeckGrade(extras[i], y0, y1);
                    n++;
                }
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
