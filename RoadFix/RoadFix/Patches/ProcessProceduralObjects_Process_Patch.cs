using System;
using HarmonyLib;
using RoadFix.Bridge;
using UnityEngine;

namespace RoadFix.Patches;

/// <summary>
/// Schedule deferred bridge cube spawn after AssetScene-props can load.
/// No path/terrain edits here.
/// </summary>
[HarmonyPatch(typeof(ProcessProceduralObjects), nameof(ProcessProceduralObjects.Process))]
public static class ProcessProceduralObjects_Process_Patch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            if (!RoadFixConfig.IsEnabled())
                return;
            if (RoadFixConfig.Config?.SpawnCustomBridges != true)
                return;

            DeferredBridgeSpawn.Schedule();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoadFix] ProcessProceduralObjects postfix failed. {ex}");
        }
    }
}
