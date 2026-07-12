using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch ProcessProceduralObjects.Process to reduce mountains if configured
    [HarmonyPatch(typeof(ProcessProceduralObjects), nameof(ProcessProceduralObjects.Process))]
    public static class ProcessProceduralObjects_Mountains_Patch
    {
        static void Postfix(ProcessProceduralObjects __instance, uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisableMountainPatch)
                return;
            if (config.TerrainConfiguration == null || config.TerrainConfiguration.MountainConfig == null)
                return;
            
            // Reduce mountains if configured
            if (config.TerrainConfiguration.MountainConfig.ReduceMountains)
            {
                var monuments = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
                if (monuments == null || monuments.Count == 0)
                    return;
                
                var mountains = new List<MonumentInfo>();
                for (int i = 0; i < monuments.Count; i++)
                {
                    var monument = monuments[i];
                    if (monument != null && monument.Type == MonumentType.Mountain)
                        mountains.Add(monument);
                }
                if (mountains.Count > 0)
                {
                    // Remove half of the mountains
                    int toRemove = mountains.Count / 2;
                    UnityEngine.Debug.Log($"[CustomMapGen] Reducing mountains - removing {toRemove} of {mountains.Count} mountains");
                    for (int i = mountains.Count - 1; i >= mountains.Count - toRemove; i--)
                    {
                        monuments.Remove(mountains[i]);
                        if (mountains[i] != null && mountains[i].gameObject != null)
                        {
                            UnityEngine.Object.Destroy(mountains[i].gameObject);
                        }
                    }
                }
            }
        }
    }
}
