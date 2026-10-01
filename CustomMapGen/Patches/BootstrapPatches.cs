using System;
using System.IO;
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
                if (config?.MapImage != null && config.MapImage.Enabled)
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Existing map detected — keeping mod loaded only to write map image (world.rendermap + cargo), then unload. Swap/recovery will not run.");
                    return;
                }
                if (CustomMapGen.ShouldKeepLoadedForDiagnostics())
                {
                    UnityEngine.Debug.Log("[CustomMapGen] Existing map detected (Bootstrap) — keeping mod loaded for diagnostics (no procgen changes, no late entity recovery).");
                    return;
                }

                UnityEngine.Debug.Log("[CustomMapGen] Existing map detected (Bootstrap) — unloading mod for this run (not a fresh wipe, diagnostics disabled).");
                CustomMapGen.TryUnloadThisMod("existing map, not a fresh wipe");
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
    }
}
