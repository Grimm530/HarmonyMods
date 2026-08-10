using HarmonyLib;
using RoadFix.Bridge;

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
        if (!RoadFixConfig.IsEnabled())
            return;
        if (RoadFixConfig.Config?.SpawnCustomBridges != true)
            return;

        DeferredBridgeSpawn.Schedule();
    }
}
