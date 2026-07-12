using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch PlaceMonuments.Process to filter monuments based on config
    [HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
    public static class PlaceMonumentsFilter_Process_Patch
    {
        static void Postfix(PlaceMonuments __instance, uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            
            var config = CustomMapGen.Instance.GetConfig();
            var monuments = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
            
            if (monuments == null || monuments.Count == 0)
                return;
            
            // Filter monuments based on config
            FilterMonumentsByConfig(monuments, config.LargeMonuments, "LargeMonuments");
            FilterMonumentsByConfig(monuments, config.SmallMonuments, "SmallMonuments");
            FilterMonumentsByConfig(monuments, config.Harbors, "Harbors");
            FilterMonumentsByConfig(monuments, config.WaterWells, "WaterWells");
            FilterMonumentsByConfig(monuments, config.Caves, "Caves");
            FilterMonumentsByConfig(monuments, config.Mountains, "Mountains");
            FilterMonumentsByConfig(monuments, config.Quarries, "Quarries");
            FilterMonumentsByConfig(monuments, config.Icebergs, "Icebergs");
            FilterMonumentsByConfig(monuments, config.IceLakes, "IceLakes");
            FilterMonumentsByConfig(monuments, config.Ruins, "Ruins");
            
            // Filter oil rigs
            if (config.OilRigConfigurations != null && config.OilRigConfigurations.Count > 0)
            {
                foreach (var oilRigConfig in config.OilRigConfigurations)
                {
                    if (string.IsNullOrEmpty(oilRigConfig.Type))
                        continue;
                    
                    var oilRigsToFilter = new List<MonumentInfo>();
                    for (int i = 0; i < monuments.Count; i++)
                    {
                        var monument = monuments[i];
                        if (monument != null && monument.IsOilRig() && monument.name.Contains(oilRigConfig.Type))
                            oilRigsToFilter.Add(monument);
                    }
                    
                    if (oilRigConfig.Blocked || !oilRigConfig.Desired)
                    {
                        foreach (var oilRig in oilRigsToFilter)
                        {
                            monuments.Remove(oilRig);
                            if (oilRig != null && oilRig.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(oilRig.gameObject);
                            }
                        }
                        if (oilRigsToFilter.Count > 0)
                        {
                            UnityEngine.Debug.Log($"[CustomMapGen] Removed {oilRigsToFilter.Count} {oilRigConfig.Type} oil rigs");
                        }
                    }
                }
            }
            
            // Filter safezones
            if (config.Safezones != null && config.Safezones.Count > 0)
            {
                foreach (var safezoneConfig in config.Safezones)
                {
                    if (string.IsNullOrEmpty(safezoneConfig.Type))
                        continue;
                    
                    var safezonesToFilter = new List<MonumentInfo>();
                    for (int i = 0; i < monuments.Count; i++)
                    {
                        var monument = monuments[i];
                        if (monument != null && monument.IsSafeZone && monument.name.Contains(safezoneConfig.Type))
                            safezonesToFilter.Add(monument);
                    }
                    
                    if (safezoneConfig.Blocked || !safezoneConfig.Desired)
                    {
                        foreach (var safezone in safezonesToFilter)
                        {
                            monuments.Remove(safezone);
                            if (safezone != null && safezone.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(safezone.gameObject);
                            }
                        }
                        if (safezonesToFilter.Count > 0)
                        {
                            UnityEngine.Debug.Log($"[CustomMapGen] Removed {safezonesToFilter.Count} {safezoneConfig.Type} safezones");
                        }
                    }
                }
            }
            
            // Filter blocked prefabs (Coastal Rocks, Rock Formations, etc.)
            if (config.BlockedPrefabs != null && config.BlockedPrefabs.Count > 0)
            {
                foreach (var blockedPrefab in config.BlockedPrefabs)
                {
                    if (string.IsNullOrEmpty(blockedPrefab))
                        continue;
                    
                    // Match by name (case-insensitive)
                    var monumentsToRemove = new List<MonumentInfo>();
                    for (int i = 0; i < monuments.Count; i++)
                    {
                        var monument = monuments[i];
                        if (monument == null)
                            continue;
                        if (monument.name.IndexOf(blockedPrefab, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            monument.transform.name.IndexOf(blockedPrefab, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            monumentsToRemove.Add(monument);
                        }
                    }
                    
                    foreach (var monument in monumentsToRemove)
                    {
                        monuments.Remove(monument);
                        if (monument != null && monument.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(monument.gameObject);
                        }
                    }
                    
                    if (monumentsToRemove.Count > 0)
                    {
                        UnityEngine.Debug.Log($"[CustomMapGen] Removed {monumentsToRemove.Count} monuments matching blocked prefab: {blockedPrefab}");
                    }
                }
            }
        }
        
        private static void FilterMonumentsByConfig(List<MonumentInfo> monuments, List<MonumentConfig> configList, string categoryName)
        {
            if (configList == null || configList.Count == 0)
                return;
            
            foreach (var monumentConfig in configList)
            {
                if (string.IsNullOrEmpty(monumentConfig.Type))
                    continue;
                
                var monumentsToFilter = new List<MonumentInfo>();
                for (int i = 0; i < monuments.Count; i++)
                {
                    var monument = monuments[i];
                    if (monument != null && monument.name.Contains(monumentConfig.Type))
                        monumentsToFilter.Add(monument);
                }
                
                if (monumentConfig.Blocked)
                {
                    // Remove blocked monuments
                    foreach (var monument in monumentsToFilter)
                    {
                        monuments.Remove(monument);
                        if (monument != null && monument.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(monument.gameObject);
                        }
                    }
                    if (monumentsToFilter.Count > 0)
                    {
                        UnityEngine.Debug.Log($"[CustomMapGen] Removed {monumentsToFilter.Count} blocked {monumentConfig.Type} monuments");
                    }
                }
                else if (monumentConfig.Desired == false)
                {
                    // Remove undesired monuments
                    foreach (var monument in monumentsToFilter)
                    {
                        monuments.Remove(monument);
                        if (monument != null && monument.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(monument.gameObject);
                        }
                    }
                    if (monumentsToFilter.Count > 0)
                    {
                        UnityEngine.Debug.Log($"[CustomMapGen] Removed {monumentsToFilter.Count} undesired {monumentConfig.Type} monuments");
                    }
                }
            }
        }
    }
}
