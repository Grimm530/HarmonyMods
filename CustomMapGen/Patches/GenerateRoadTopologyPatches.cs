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
                    // Construction.TestPlacingCloseToRoad blocks on Road (2048) | Powerline (524288), not Roadside (4096).
                    // 0x31 is Field | Beach | Forest, the same cells vanilla will paint as roadside.
                    const int Road = 2048;
                    const int BuildableGround = 0x31;
                    const int Building = 0x200000;
                    int cleared = 0;
                    int leftBlocked = 0;
                    for (int z = 0; z < res; z++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int index = z * res + x;
                            int bits = map[index];
                            if ((bits & Road) == 0)
                                continue;
                            if ((bits & BuildableGround) == 0)
                            {
                                leftBlocked++;
                                continue;
                            }
                            bits &= ~Road;
                            bits |= Building;
                            map[index] = bits;
                            cleared++;
                        }
                    }
                    UnityEngine.Debug.Log($"[CustomMapGen] Allow building on roads: cleared the road bit on {cleared} field/beach/forest cells. {leftBlocked} road cells stayed blocked (cliff, ocean, monument, or other ground).");
                }
            }
        }
    }
}
