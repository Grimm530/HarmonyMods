using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch PlaceMonumentsRoadside.Process to filter car wrecks if configured
    [HarmonyPatch(typeof(PlaceMonumentsRoadside), nameof(PlaceMonumentsRoadside.Process))]
    public static class PlaceMonumentsRoadside_Process_Patch
    {
        static void Postfix(PlaceMonumentsRoadside __instance, uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisablePlaceMonumentsRoadsidePatch)
                return;
            var monuments = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
            
            if (monuments == null || monuments.Count == 0)
                return;
            
            // Remove car wrecks if configured (fallback: WorldAddPrefabPatches blocks most at source)
            if (config.RemoveCarWrecks)
            {
                var wrecksToRemove = new List<MonumentInfo>();
                for (int i = 0; i < monuments.Count; i++)
                {
                    var monument = monuments[i];
                    if (monument != null && IsCarWreckName(monument.name))
                        wrecksToRemove.Add(monument);
                }
                if (wrecksToRemove.Count > 0)
                {
                    UnityEngine.Debug.Log($"[CustomMapGen] Removing {wrecksToRemove.Count} car wrecks from roads");
                    foreach (var wreck in wrecksToRemove)
                    {
                        monuments.Remove(wreck);
                        if (wreck != null && wreck.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(wreck.gameObject);
                        }
                    }
                }
            }
        }

        static bool IsCarWreckName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            return n.Contains("wreck") || n.Contains("vehicle_wreck") || n.Contains("car_wreck");
        }
    }
}
