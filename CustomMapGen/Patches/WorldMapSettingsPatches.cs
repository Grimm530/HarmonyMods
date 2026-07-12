using System;
using System.IO;
using HarmonyLib;
using ConVar;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Override World name, map/save folder, and map/save file name when MapSettings overrides are set (HarmonyCustomGenerator parity).
    /// </summary>
    [HarmonyPatch(typeof(World), "get_Name")]
    public static class World_get_Name_Patch
    {
        static void Postfix(ref string __result)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DisableWorldMapSettingsPatch == true)
                return;
            if (config?.MapSettings == null || !config.MapSettings.Enabled || string.IsNullOrEmpty(config.MapSettings.SaveNameOverride))
                return;
            __result = config.MapSettings.SaveNameOverride;
        }
    }

    [HarmonyPatch(typeof(World), "get_MapFolderName")]
    public static class World_get_MapFolderName_Patch
    {
        static void Postfix(ref string __result)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DisableWorldMapSettingsPatch == true)
                return;
            if (config?.MapSettings == null || !config.MapSettings.Enabled || string.IsNullOrEmpty(config.MapSettings.SaveFolderOverride))
                return;
            string folder = config.MapSettings.SaveFolderOverride;
            __result = string.IsNullOrEmpty(Path.GetPathRoot(folder)) ? Path.Combine(Environment.CurrentDirectory, folder) : folder;
        }
    }

    [HarmonyPatch(typeof(World), "get_SaveFolderName")]
    public static class World_get_SaveFolderName_Patch
    {
        static void Postfix(ref string __result)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DisableWorldMapSettingsPatch == true)
                return;
            if (config?.MapSettings == null || !config.MapSettings.Enabled || string.IsNullOrEmpty(config.MapSettings.SaveFolderOverride))
                return;
            string folder = config.MapSettings.SaveFolderOverride;
            __result = string.IsNullOrEmpty(Path.GetPathRoot(folder)) ? Path.Combine(Environment.CurrentDirectory, folder) : folder;
        }
    }

    [HarmonyPatch(typeof(World), "get_MapFileName")]
    public static class World_get_MapFileName_Patch
    {
        static void Postfix(ref string __result)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DisableWorldMapSettingsPatch == true)
                return;
            if (config?.MapSettings == null || !config.MapSettings.Enabled || string.IsNullOrEmpty(config.MapSettings.SaveNameOverride))
                return;
            string name = config.MapSettings.SaveNameOverride.Replace(" ", "").ToLower();
            __result = name + "." + World.Size + "." + World.Seed + "." + 273 + ".map";
        }
    }

    [HarmonyPatch(typeof(World), "get_SaveFileName")]
    public static class World_get_SaveFileName_Patch
    {
        static void Postfix(ref string __result)
        {
            if (CustomMapGen.IsLoadingExistingMap) return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.DisableWorldMapSettingsPatch == true)
                return;
            if (config?.MapSettings == null || !config.MapSettings.Enabled || string.IsNullOrEmpty(config.MapSettings.SaveNameOverride))
                return;
            string name = config.MapSettings.SaveNameOverride.Replace(" ", "").ToLower();
            __result = name + "." + World.Size + "." + World.Seed + "." + 273 + ".sav";
        }
    }
}
