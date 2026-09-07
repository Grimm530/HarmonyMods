using System;
using System.Collections;
using System.IO;
using CustomMapGen.Utility;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Dedicated servers call <see cref="UI_LoadingScreen.Update(string)"/> (the method body is empty).
    /// HarmonyCustomGenerator patched the client-only <c>LoadingScreen</c> type, which never runs here.
    /// </summary>
    [HarmonyPatch(typeof(UI_LoadingScreen), nameof(UI_LoadingScreen.Update), new Type[] { typeof(string) })]
    public static class UI_LoadingScreen_Update_MapImage_Patch
    {
        internal static bool RanThisGen;

        static void Prefix(string strType)
        {
            if (strType != "DONE")
                return;
            if (RanThisGen)
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            if (TerrainTexturing.Instance == null)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] Map image skipped: TerrainTexturing.Instance is null at DONE.");
                return;
            }

            var config = CustomMapGen.Instance?.GetConfig();
            if (config?.MapImage == null || !config.MapImage.Enabled)
            {
                MaybeQuit(config);
                return;
            }

            RanThisGen = true;
            UnityEngine.Debug.Log($"[CustomMapGen] SIZE: {World.Size} | SEED: {World.Seed} | Cached={World.Cached}");
            GenerateMapImageSync(config);
            MaybeQuit(config);
        }

        internal static string GetPngPath(MapGenConfig config)
        {
            string dir = string.IsNullOrEmpty(config.MapImage.OutputFolder)
                ? Environment.CurrentDirectory
                : Path.Combine(Environment.CurrentDirectory, config.MapImage.OutputFolder);
            string fileName = config.MapImage.MapVoterFormat
                ? $"{World.Size}_{World.Seed}.png"
                : $"map_{World.Size}_{World.Seed}.png";
            return Path.Combine(dir, fileName);
        }

        static void MaybeQuit(MapGenConfig config)
        {
            if (config == null || !config.QuitAfterMapCreation)
                return;
            if (World.Cached || CustomMapGen.IsLoadingExistingMap)
                return;

            float delay = Mathf.Max(0f, config.QuitAfterMapCreationDelaySeconds);
            UnityEngine.Debug.Log($"[CustomMapGen] Map generation complete. Quitting in {delay:0.#}s so the next start loads the saved map.");
            if (delay <= 0f)
            {
                Application.Quit();
                return;
            }

            var host = new GameObject("CustomMapGen_QuitAfterMapCreation");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<QuitAfterDelay>().Begin(delay);
        }

        static void GenerateMapImageSync(MapGenConfig config)
        {
            string fontResourcesPath = string.IsNullOrWhiteSpace(config.MapImage.FontResourcesPath)
                ? "maps/images/resources"
                : config.MapImage.FontResourcesPath.Trim();
            MapImageRenderFull.EnsureFontDirs(fontResourcesPath);

            float scale = Mathf.Clamp(config.MapImage.Scale, 0.1f, 4f);
            int oceanMargin = Mathf.Clamp(config.MapImage.OceanMargin, 100, 500);
            string preferredFont = string.IsNullOrWhiteSpace(config.MapImage.MonumentFont) ? null : config.MapImage.MonumentFont.Trim();

            UnityEngine.Debug.Log($"[CustomMapGen] Map image: MapImageRenderer (world.rendermap) scale={scale} margin={oceanMargin}");
            byte[] png = MapImageRenderer.Render(out int imageWidth, out int imageHeight, out _,
                scale, false, false, oceanMargin);
            if (png == null || png.Length == 0)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] MapImageRenderer.Render returned null.");
                return;
            }

            png = MapImageOverlay.ApplyOverlays(png, oceanMargin,
                config.MapImage.IncludeMonumentNames, config.MapImage.IncludeGrid, config.MapImage.IncludeCargoPath,
                fontResourcesPath, preferredFont);

            string fullPath = GetPngPath(config);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(fullPath, png);
            UnityEngine.Debug.Log($"[CustomMapGen] Map image saved: {fullPath} ({imageWidth}x{imageHeight})");
        }

        private sealed class QuitAfterDelay : MonoBehaviour
        {
            public void Begin(float seconds)
            {
                StartCoroutine(Run(seconds));
            }

            IEnumerator Run(float seconds)
            {
                yield return new WaitForSecondsRealtime(seconds);
                Application.Quit();
            }
        }
    }
}
