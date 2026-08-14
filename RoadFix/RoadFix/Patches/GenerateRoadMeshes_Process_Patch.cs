using System;
using System.Collections.Generic;
using HarmonyLib;
using RoadFix.Bridge;
using UnityEngine;

namespace RoadFix.Patches;

/// <summary>
/// Only job vs vanilla: rebuild road meshes with snapToTerrain=false so path-node Y
/// (already correct from procgen) is kept on every load — including RunOnCache.
/// Also queues road bridges on fresh gen. Never moves path nodes.
/// </summary>
[HarmonyPatch(typeof(GenerateRoadMeshes), nameof(GenerateRoadMeshes.Process))]
public static class GenerateRoadMeshes_Process_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(GenerateRoadMeshes __instance, uint seed)
    {
        try
        {
            if (!RoadFixConfig.IsEnabled())
                return true;

            var cfg = RoadFixConfig.Config;

            if (cfg.SpawnCustomBridges)
            {
                if (BridgeService.RoadCrossingCount == 0)
                    BridgeService.PrepareRoadCrossings();
                BridgeService.PlaceRoadBridgesOnly();
            }

            if (cfg.RoadsSnapToTerrain)
                return true;

            List<PathList> roads = TerrainPathAccess.GetRoads(TerrainMeta.Path);
            if (roads == null)
                return true;

            if (__instance.RoadMeshes == null || __instance.RoadMeshes.Length == 0)
                __instance.RoadMeshes = new Mesh[1] { __instance.RoadMesh };

            int meshCount = 0;
            foreach (PathList road in roads)
            {
                if (road.Hierarchy >= 2)
                    continue;

                GameObject gameObject = new GameObject(road.Name);
                foreach (PathList.MeshObject item in road.CreateMesh(
                    __instance.RoadMeshes,
                    0f,
                    snapToTerrain: false,
                    snapStartToTerrain: false,
                    snapEndToTerrain: false))
                {
                    GameObject obj = new GameObject("Road Mesh");
                    obj.transform.position = item.Position;
                    obj.layer = 16;
                    obj.tag = "IgnoreCollider";
                    obj.transform.SetParent(gameObject.transform, worldPositionStays: true);
                    obj.SetActive(value: false);
                    MeshCollider meshCollider = obj.AddComponent<MeshCollider>();
                    meshCollider.sharedMaterial = __instance.RoadPhysicMaterial;
                    meshCollider.sharedMesh = item.Meshes[0];
                    TagComponentEx.SetCustomTag(obj, GameObjectTag.Road, apply: true);
                    // Skip AddToHeightMap on bridge spans so meshes don't refill the river bed.
                    if (!BridgeService.IsNearRoadCrossing(item.Position))
                        obj.AddComponent<AddToHeightMap>();
                    obj.SetActive(value: true);
                    meshCount++;
                }
            }

            if (cfg.DebugLogging)
            {
                Debug.Log(
                    $"[RoadFix] GenerateRoadMeshes: {meshCount} segments, snapToTerrain=false, " +
                    $"roadCrossings={BridgeService.RoadCrossingCount}");
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoadFix] GenerateRoadMeshes prefix failed; falling back to vanilla. {ex}");
            return true;
        }
    }
}
