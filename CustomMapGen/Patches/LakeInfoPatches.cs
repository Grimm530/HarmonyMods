using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch ProcessProceduralObjects.Process to clean up lakes after they're all processed
    [HarmonyPatch(typeof(ProcessProceduralObjects), nameof(ProcessProceduralObjects.Process))]
    public static class ProcessProceduralObjects_Process_Patch
    {
        static void Postfix(ProcessProceduralObjects __instance, uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisableLakeInfoPatch)
                return;
            // Clean up lakes after all procedural objects are processed
            {
                var lakeObjs = TerrainPathAccess.GetLakeObjs(TerrainMeta.Path);
                
                if (lakeObjs == null || lakeObjs.Count == 0)
                    return;
                
                // Check if lakes are blocked or not wanted
                if (config.LakesBlocked || config.LakesGenerate == "NotWanted")
                {
                    UnityEngine.Debug.Log($"[CustomMapGen] Lakes disabled - removing {lakeObjs.Count} lakes");
                    // Remove all lakes
                    for (int i = lakeObjs.Count - 1; i >= 0; i--)
                    {
                        var lake = lakeObjs[i];
                        if (lake != null && lake.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(lake.gameObject);
                        }
                    }
                    lakeObjs.Clear();
                    return;
                }
                
                // Limit lake count using min/max range
                if (config.LakesGenerate == "Wanted" && lakeObjs.Count > config.LakeMaxAmount)
                {
                    int toRemove = lakeObjs.Count - config.LakeMaxAmount;
                    UnityEngine.Debug.Log($"[CustomMapGen] Lake count limit reached ({config.LakeMaxAmount}) - removing {toRemove} excess lakes");
                    
                    // Remove excess lakes (remove from end)
                    for (int i = lakeObjs.Count - 1; i >= config.LakeMaxAmount; i--)
                    {
                        var lake = lakeObjs[i];
                        if (lake != null && lake.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(lake.gameObject);
                        }
                        lakeObjs.RemoveAt(i);
                    }
                }
                
                // Ensure minimum amount (this would require spawning more, which is complex)
                // For now, we just enforce the maximum
            }
        }
    }
}
