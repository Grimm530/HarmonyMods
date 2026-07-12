using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace CustomMapGen.Patches
{
    // Patch TerrainMeta.Init to set custom axis angles using reflection
    [HarmonyPatch(typeof(TerrainMeta), nameof(TerrainMeta.Init))]
    public static class TerrainMeta_Init_Patch
    {
        static void Postfix(TerrainMeta __instance, object __0, TerrainConfig configOverride)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            
            var config = CustomMapGen.Instance.GetConfig();
            if (config.TerrainConfiguration == null)
                return;

            if (!string.IsNullOrEmpty(config.TerrainConfiguration.BiomeAxisAngle))
            {
                float biomeAngle = ParseAxisAngle(config.TerrainConfiguration.BiomeAxisAngle);
                if (biomeAngle >= 0f)
                {
                    var biomeField = typeof(TerrainMeta).GetField("BiomeAxisAngle", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (biomeField != null)
                    {
                        biomeField.SetValue(null, biomeAngle);
                        UnityEngine.Debug.Log($"[CustomMapGen] Set BiomeAxisAngle to {biomeAngle} from config: {config.TerrainConfiguration.BiomeAxisAngle}");
                    }
                }
            }

            if (!string.IsNullOrEmpty(config.TerrainConfiguration.LootAxisAngle))
            {
                float lootAngle = ParseAxisAngle(config.TerrainConfiguration.LootAxisAngle);
                if (lootAngle >= 0f)
                {
                    var lootField = typeof(TerrainMeta).GetField("LootAxisAngle", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (lootField != null)
                    {
                        lootField.SetValue(null, lootAngle);
                        UnityEngine.Debug.Log($"[CustomMapGen] Set LootAxisAngle to {lootAngle} from config: {config.TerrainConfiguration.LootAxisAngle}");
                    }
                }
            }
        }
        
        private static float ParseAxisAngle(string angleString)
        {
            // Parse common axis angle strings
            // "TopDesertBottomSnow" = 0 degrees (top is desert, bottom is snow)
            // "LeftTier0RightTier2" = 90 degrees (left is tier 0, right is tier 2)
            // Can also be numeric string
            if (float.TryParse(angleString, out float numericAngle))
            {
                return numericAngle;
            }
            
            // Map common strings to angles
            switch (angleString.ToLower())
            {
                case "topdesertbottomsnow":
                case "topdesert":
                    return 0f;
                case "lefttier0righttier2":
                case "lefttier0":
                    return 90f;
                case "righttier0lefttier2":
                case "righttier0":
                    return 270f;
                case "bottomsnowtopdesert":
                case "bottomsnow":
                    return 180f;
                default:
                    UnityEngine.Debug.LogWarning($"[CustomMapGen] Unknown axis angle string: {angleString}");
                    return -1f;
            }
        }
    }
}
