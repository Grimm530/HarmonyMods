using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ConVar;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// First thing: detect if procedural map file already exists (not a fresh wipe). If so, unload this mod
    /// so no patches run at all (avoids any load issues). Otherwise apply map size override (3500-6000) when MapSettings enabled.
    /// </summary>
    [HarmonyPatch(typeof(Bootstrap), "DedicatedServerStartup")]
    public static class Bootstrap_DedicatedServerStartup_Patch
    {
        private const string ModName = "CustomMapGen";

        static void Prefix()
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;

            // Detect existing map as early as possible — if present, unload the mod so no patches run.
            // If the server's save folder has any .map file, the server has a map (version number changes every wipe).
            string rootFolder = Server.rootFolder ?? "server/" + (Server.identity ?? "");
            string folder = string.IsNullOrEmpty(Path.GetPathRoot(rootFolder)) ? Path.Combine(Environment.CurrentDirectory, rootFolder) : rootFolder;
            bool exists = Directory.Exists(folder) && Directory.GetFiles(folder, "*.map").Length > 0;
            var config = CustomMapGen.Instance?.GetConfig();
            if (!exists && config?.MapSettings != null && config.MapSettings.Enabled && !string.IsNullOrEmpty(config.MapSettings.SaveFolderOverride))
            {
                string customFolder = string.IsNullOrEmpty(Path.GetPathRoot(config.MapSettings.SaveFolderOverride)) ? Path.Combine(Environment.CurrentDirectory, config.MapSettings.SaveFolderOverride) : config.MapSettings.SaveFolderOverride;
                exists = Directory.Exists(customFolder) && Directory.GetFiles(customFolder, "*.map").Length > 0;
            }
            CustomMapGen.SetIsLoadingExistingMap(exists);
            if (exists)
            {
                bool keepLoadedForDiagnostics =
                    config?.DebugLogging == true &&
                    (config.DebugLogSkippedWorldPrefabs || config.DebugLogSwapMapPrefabBreakdown);
                if (keepLoadedForDiagnostics)
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Existing map detected (Bootstrap) — keeping mod loaded for diagnostics (no procgen changes).");
                    return;
                }

                UnityEngine.Debug.Log("[CustomMapGen] Existing map detected (Bootstrap) — unloading mod for this run (not a fresh wipe, diagnostics disabled).");
                TryUnloadThisMod();
                return;
            }

            if (config?.MapSettings == null || !config.MapSettings.Enabled)
                return;
            var ms = config.MapSettings;
            if (ms.MapSizeOverride > 0)
            {
                int size = Mathf.Clamp(ms.MapSizeOverride, 3500, 6000);
                Server.worldsize = size;
                UnityEngine.Debug.Log($"[CustomMapGen] Map size override: {size}");
            }
        }

        /// <summary>Call HarmonyLoader.TryUnloadMod(ModName) via reflection so this mod is fully unloaded when existing map is present.</summary>
        private static void TryUnloadThisMod()
        {
            try
            {
                Type loaderType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        loaderType = asm.GetType("HarmonyLoader");
                        if (loaderType != null) break;
                    }
                    catch { }
                }
                if (loaderType == null)
                {
                    UnityEngine.Debug.LogWarning("[CustomMapGen] HarmonyLoader type not found; cannot unload mod.");
                    return;
                }
                var method = loaderType.GetMethod("TryUnloadMod", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
                if (method == null)
                {
                    UnityEngine.Debug.LogWarning("[CustomMapGen] HarmonyLoader.TryUnloadMod not found; cannot unload mod.");
                    return;
                }
                bool result = (bool)method.Invoke(null, new object[] { ModName });
                if (result)
                    UnityEngine.Debug.Log("[CustomMapGen] Mod unloaded (harmony.unload " + ModName + ").");
                else
                    UnityEngine.Debug.LogWarning("[CustomMapGen] TryUnloadMod returned false (mod may not have been loaded).");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] Failed to unload mod: " + ex.Message);
            }
        }
    }
}
