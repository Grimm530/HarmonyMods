using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Apply CustomMapGen settings after WorldConfig is loaded from server/config file.
    /// Also applied after LoadScriptableConfigs so we run when no server config string is used.
    /// </summary>
    [HarmonyPatch(typeof(WorldConfig), nameof(WorldConfig.LoadFromWorldConfig))]
    public static class WorldConfig_LoadFromWorldConfig_Patch
    {
        static void Postfix(WorldConfig __instance, WorldConfig data)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            WorldConfigPatches_Apply.ApplyConfigToWorldConfig(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldConfig), nameof(WorldConfig.LoadScriptableConfigs))]
    public static class WorldConfig_LoadScriptableConfigs_Patch
    {
        static void Postfix(WorldConfig __instance)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            WorldConfigPatches_Apply.ApplyConfigToWorldConfig(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldConfig), nameof(WorldConfig.LoadFromJsonString))]
    public static class WorldConfig_LoadFromJsonString_Patch
    {
        static void Postfix(WorldConfig __instance)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            WorldConfigPatches_Apply.ApplyConfigToWorldConfig(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldConfig), nameof(WorldConfig.LoadFromJsonFile))]
    public static class WorldConfig_LoadFromJsonFile_Patch
    {
        static void Postfix(WorldConfig __instance)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            WorldConfigPatches_Apply.ApplyConfigToWorldConfig(__instance);
        }
    }

    internal static class WorldConfigPatches_Apply
    {
        internal static void ApplyConfigToWorldConfig(WorldConfig __instance)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance.GetConfig();

            __instance.Powerlines = config.Powerlines;

            if (config.GenerateAboveGroundTrainTracks == "NotWanted")
                __instance.AboveGroundRails = false;
            else if (config.GenerateAboveGroundTrainTracks == "Wanted")
                __instance.AboveGroundRails = true;

            if (config.RemoveRivers)
                __instance.Rivers = false;

            if (config.UnderwaterLabsBlocked || config.UnderwaterLabsGenerate == "NotWanted")
                __instance.UnderwaterLabs = false;
            else if (config.UnderwaterLabsGenerate == "Wanted")
                __instance.UnderwaterLabs = true;

            if (config.RemoveUndergroundTunnels)
                __instance.BelowGroundRails = false;

            // Apply Tier and/or Biome from config. ApplyTierBiomeToWorldConfig = both; otherwise ApplyTierToWorldConfig / ApplyBiomeToWorldConfig allow tier-only or biome-only (biome-only may avoid mainland ocean topology bug).
            bool applyTier = config.ApplyTierBiomeToWorldConfig || config.ApplyTierToWorldConfig;
            bool applyBiome = config.ApplyTierBiomeToWorldConfig || config.ApplyBiomeToWorldConfig;
            if (applyTier && config.TerrainConfiguration?.TierConfig != null && config.TerrainConfiguration.TierConfig.Enabled)
            {
                __instance.PercentageTier0 = config.TerrainConfiguration.TierConfig.Tier0Percentage;
                __instance.PercentageTier1 = config.TerrainConfiguration.TierConfig.Tier1Percentage;
                __instance.PercentageTier2 = config.TerrainConfiguration.TierConfig.Tier2Percentage;
            }

            if (applyBiome && config.TerrainConfiguration?.BiomeConfig != null && config.TerrainConfiguration.BiomeConfig.Enabled)
            {
                __instance.PercentageBiomeArid = config.TerrainConfiguration.BiomeConfig.AridPercentage;
                __instance.PercentageBiomeTemperate = config.TerrainConfiguration.BiomeConfig.TemperatePercentage;
                __instance.PercentageBiomeTundra = config.TerrainConfiguration.BiomeConfig.TundraPercentage;
                __instance.PercentageBiomeArctic = config.TerrainConfiguration.BiomeConfig.ArcticPercentage;
                __instance.PercentageBiomeJungle = config.TerrainConfiguration.BiomeConfig.JunglePercentage;
            }

            if (config.BlockedPrefabs != null && config.BlockedPrefabs.Count > 0)
            {
                foreach (var blockedPrefab in config.BlockedPrefabs)
                {
                    if (!__instance.PrefabBlacklist.Contains(blockedPrefab))
                        __instance.PrefabBlacklist.Add(blockedPrefab);
                }
            }
        }
    }
}
