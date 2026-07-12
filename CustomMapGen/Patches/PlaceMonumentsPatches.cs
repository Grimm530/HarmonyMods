using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>Apply MinMonumentDistance to PlaceMonuments so different monument types (e.g. oasis and outpost) stay at least this many meters apart.</summary>
    [HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
    [HarmonyPriority(HarmonyLib.Priority.First)]
    public static class PlaceMonuments_Process_MinDistance_Patch
    {
        static void Prefix(PlaceMonuments __instance)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.MinMonumentDistance <= 0)
                return;
            __instance.MinDistanceDifferentType = config.MinMonumentDistance;
            if (config.DebugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] PlaceMonuments MinDistanceDifferentType set to {config.MinMonumentDistance}m");
        }
    }

    // Patch PlaceMonuments.Process to filter oases and canyons based on config
    [HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
    public static class PlaceMonuments_Process_Patch
    {
        static void Postfix(PlaceMonuments __instance, uint seed)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            var config = CustomMapGen.Instance.GetConfig();
            if (config.DisablePlaceMonumentsPatch)
                return;
            var monuments = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
            
            if (monuments == null || monuments.Count == 0)
                return;
            
            // Filter oases
            if (config.OasesBlocked || config.OasesGenerate == "NotWanted")
            {
                var oasesToRemove = new List<MonumentInfo>();
                for (int i = 0; i < monuments.Count; i++)
                {
                    var monument = monuments[i];
                    if (monument != null && monument.Type == MonumentType.Oasis)
                        oasesToRemove.Add(monument);
                }
                if (oasesToRemove.Count > 0)
                {
                    UnityEngine.Debug.Log($"[CustomMapGen] Removing {oasesToRemove.Count} oases");
                    foreach (var oasis in oasesToRemove)
                    {
                        monuments.Remove(oasis);
                        if (oasis != null && oasis.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(oasis.gameObject);
                        }
                    }
                }
            }
            else
            {
                int oasisCount = 0;
                for (int i = 0; i < monuments.Count; i++)
                {
                    var monument = monuments[i];
                    if (monument != null && monument.Type == MonumentType.Oasis)
                        oasisCount++;
                }
                if (config.OasesGenerate == "Wanted" && oasisCount > config.OasesMaxAmount)
                {
                    var oases = new List<MonumentInfo>();
                    for (int i = 0; i < monuments.Count; i++)
                    {
                        var monument = monuments[i];
                        if (monument != null && monument.Type == MonumentType.Oasis)
                            oases.Add(monument);
                    }
                    int toRemove = oases.Count - config.OasesMaxAmount;
                    UnityEngine.Debug.Log($"[CustomMapGen] Limiting oases to {config.OasesMaxAmount} - removing {toRemove} excess");
                    for (int i = oases.Count - 1; i >= config.OasesMaxAmount; i--)
                    {
                        monuments.Remove(oases[i]);
                        if (oases[i] != null && oases[i].gameObject != null)
                        {
                            UnityEngine.Object.Destroy(oases[i].gameObject);
                        }
                    }
                }
            }
            
            // Filter canyons
            if (config.CanyonsBlocked || config.CanyonsGenerate == "NotWanted")
            {
                var canyonsToRemove = new List<MonumentInfo>();
                for (int i = 0; i < monuments.Count; i++)
                {
                    var monument = monuments[i];
                    if (monument != null && monument.Type == MonumentType.Canyon)
                        canyonsToRemove.Add(monument);
                }
                if (canyonsToRemove.Count > 0)
                {
                    UnityEngine.Debug.Log($"[CustomMapGen] Removing {canyonsToRemove.Count} canyons");
                    foreach (var canyon in canyonsToRemove)
                    {
                        monuments.Remove(canyon);
                        if (canyon != null && canyon.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(canyon.gameObject);
                        }
                    }
                }
            }
            else
            {
                int canyonCount = 0;
                for (int i = 0; i < monuments.Count; i++)
                {
                    var monument = monuments[i];
                    if (monument != null && monument.Type == MonumentType.Canyon)
                        canyonCount++;
                }
                if (config.CanyonsGenerate == "Wanted" && canyonCount > config.CanyonsMaxAmount)
                {
                    var canyons = new List<MonumentInfo>();
                    for (int i = 0; i < monuments.Count; i++)
                    {
                        var monument = monuments[i];
                        if (monument != null && monument.Type == MonumentType.Canyon)
                            canyons.Add(monument);
                    }
                    int toRemove = canyons.Count - config.CanyonsMaxAmount;
                    UnityEngine.Debug.Log($"[CustomMapGen] Limiting canyons to {config.CanyonsMaxAmount} - removing {toRemove} excess");
                    for (int i = canyons.Count - 1; i >= config.CanyonsMaxAmount; i--)
                    {
                        monuments.Remove(canyons[i]);
                        if (canyons[i] != null && canyons[i].gameObject != null)
                        {
                            UnityEngine.Object.Destroy(canyons[i].gameObject);
                        }
                    }
                }
            }
        }
    }
}
