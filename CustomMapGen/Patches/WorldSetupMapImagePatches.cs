using System;
using System.IO;
using System.Reflection;
using CustomMapGen.Utility;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Map image: Run at LoadingScreen.Update("DONE") - AFTER Finalizing World, matching HarmonyCustomGenerator.
    /// Terrain data is still in memory at DONE; rendering here produces the same quality as HCG.
    /// LoadingScreen may not exist on dedicated server builds; TargetMethod returns a no-op then so Harmony does not throw.
    /// </summary>
    [HarmonyPatch]
    public static class LoadingScreen_Update_MapImage_Patch
    {
        static MethodBase TargetMethod()
        {
            Type loadingScreenType = AccessTools.TypeByName("LoadingScreen");
            if (loadingScreenType == null)
                return AccessTools.Method(typeof(LoadingScreen_Update_MapImage_Patch), nameof(NoOp), new Type[] { typeof(string) });
            MethodBase target = AccessTools.Method(loadingScreenType, "Update", new Type[] { typeof(string) });
            return target ?? AccessTools.Method(typeof(LoadingScreen_Update_MapImage_Patch), nameof(NoOp), new Type[] { typeof(string) });
        }

        static void NoOp(string strType) { }

        static void Prefix(string strType, MethodBase __originalMethod)
        {
            if (__originalMethod?.DeclaringType == typeof(LoadingScreen_Update_MapImage_Patch))
                return;
            if (strType != "DONE")
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled() || !World.Procedural || World.Cached)
                return;
            if (TerrainTexturing.Instance == null)
                return;

            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.MapImage == null || !config.MapImage.Enabled)
            {
                QuitIfNeeded(config);
                return;
            }

            UnityEngine.Debug.Log($"[CustomMapGen] SIZE: {World.Size} | SEED: {World.Seed}");
            GenerateMapImageSync(config);
            QuitIfNeeded(config);
        }

        private static void QuitIfNeeded(MapGenConfig config)
        {
            UnityEngine.Debug.Log("[CustomMapGen] Map generation complete. Quitting to avoid freeze. Restart server to load the map.");
            Application.Quit();
        }

        private static void GenerateMapImageSync(MapGenConfig config)
        {
            string fontResourcesPath = string.IsNullOrWhiteSpace(config.MapImage.FontResourcesPath)
                ? "maps/images/resources"
                : config.MapImage.FontResourcesPath.Trim();
            MapImageRenderFull.EnsureFontDirs(fontResourcesPath);

            float scale = Mathf.Clamp(config.MapImage.Scale, 0.1f, 4f);
            bool includeMonuments = config.MapImage.IncludeMonumentNames;
            bool includeGrid = config.MapImage.IncludeGrid;
            int oceanMargin = Mathf.Clamp(config.MapImage.OceanMargin, 100, 500);
            string preferredFont = string.IsNullOrWhiteSpace(config.MapImage.MonumentFont) ? null : config.MapImage.MonumentFont.Trim();

            byte[] png = MapImageRenderFull.Render(out int imageWidth, out int imageHeight, out UnityEngine.Color background,
                scale, includeMonuments, includeGrid, oceanMargin, fontResourcesPath, preferredFont);

            if (png == null || png.Length == 0)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] Map image render returned null.");
                return;
            }

            string dir = string.IsNullOrEmpty(config.MapImage.OutputFolder)
                ? Environment.CurrentDirectory
                : Path.Combine(Environment.CurrentDirectory, config.MapImage.OutputFolder);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string fileName = config.MapImage.MapVoterFormat
                ? $"{World.Size}_{World.Seed}.png"
                : $"map_{World.Size}_{World.Seed}.png";
            string fullPath = Path.Combine(dir, fileName);
            File.WriteAllBytes(fullPath, png);
            UnityEngine.Debug.Log($"[CustomMapGen] Map image saved: {fullPath}");
        }
    }
}
