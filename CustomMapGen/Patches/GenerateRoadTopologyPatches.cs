using HarmonyLib;
using System.Reflection;
using Unity.Collections;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch GenerateRoadTopology.MarkRoadside to allow building on roads if configured
    [HarmonyPatch(typeof(GenerateRoadTopology), "MarkRoadside")]
    public static class GenerateRoadTopology_MarkRoadside_Patch
    {
        static void Postfix(GenerateRoadTopology __instance)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisableRoadTopologyPatch)
                return;
            if (config.AllowBuildingOnRoads)
            {
                TerrainTopologyMap topomap = TerrainMeta.TopologyMap;
                var dstField = typeof(TerrainTopologyMap).BaseType.GetField("dst", BindingFlags.NonPublic | BindingFlags.Instance);
                var resField = typeof(TerrainTopologyMap).BaseType.GetField("res", BindingFlags.NonPublic | BindingFlags.Instance);
                if (dstField != null && resField != null)
                {
                    NativeArray<int> map = (NativeArray<int>)dstField.GetValue(topomap);
                    int res = (int)resField.GetValue(topomap);
                    for (int z = 0; z < res; z++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int index = z * res + x;
                            map[index] &= ~4096;
                        }
                    }
                    UnityEngine.Debug.Log("[CustomMapGen] Removed roadside topology flags to allow building on roads");
                }
            }
        }
    }
}
