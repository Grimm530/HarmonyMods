using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CustomMapGen.Utility
{
    /// <summary>
    /// Adds monument labels and grid overlay to a base map image (from MapImageRenderer).
    /// Ported from HarmonyCustomGenerator. Requires System.Drawing.
    /// </summary>
    public static class MapImageOverlay
    {
        private const int OceanMargin = 500;
        private const float GridCellSize = 146.3f; // meters per grid cell
        private const float BrightnessBoost = 1.25f;  // Brighten dim server-rendered map to match client quality
        private const float ContrastBoost = 1.08f;
        private const float MonumentFontSize = 26f;   // Larger, more readable monument labels
        private static readonly string FontFolder = "maps/images/resources";
        private static readonly string MonumentFont = "PermanentMarker.ttf";
        private static readonly string[] FallbackFontNames = { "Segoe UI Semibold", "Segoe UI", "Calibri", "Arial" };

        /// <summary>
        /// Apply monument names and/or grid overlay to the base PNG. Returns modified PNG bytes.
        /// </summary>
        public static byte[] ApplyOverlays(byte[] basePng, int imageWidth, int imageHeight, float scale,
            bool includeMonumentNames, bool includeGrid)
        {
            if (basePng == null || basePng.Length == 0)
                return basePng;
            if (!includeMonumentNames && !includeGrid)
                return basePng;

            using var ms = new MemoryStream(basePng);
            using var srcBmp = new Bitmap(ms);
            using var bmp = ApplyBrightnessContrast(srcBmp, BrightnessBoost, ContrastBoost);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            int mapRes = imageWidth - OceanMargin * 2;
            int mapSize = (int)World.Size;

            if (includeGrid)
            {
                RenderGrid(g, bmp, mapSize, mapRes, imageWidth);
            }

            if (includeMonumentNames)
            {
                RenderMonumentLabels(g, bmp, mapSize, mapRes, imageWidth);
            }

            using var outMs = new MemoryStream();
            bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
            return outMs.ToArray();
        }

        /// <summary>
        /// Brighten and boost contrast to counteract dim server-rendered map output.
        /// </summary>
        private static Bitmap ApplyBrightnessContrast(Bitmap source, float brightness, float contrast)
        {
            var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var attrs = new ImageAttributes();
                // Brightness: scale RGB. Contrast: (x-0.5)*c+0.5 => scale c, offset 0.5*(1-c), then * brightness
                float c = contrast;
                float off = brightness * 0.5f * (1f - c);
                var matrix = new ColorMatrix(new float[][] {
                    new float[] { brightness * c, 0f, 0f, 0f, 0f },
                    new float[] { 0f, brightness * c, 0f, 0f, 0f },
                    new float[] { 0f, 0f, brightness * c, 0f, 0f },
                    new float[] { 0f, 0f, 0f, 1f, 0f },
                    new float[] { off, off, off, 0f, 1f }
                });
                attrs.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
            }
            return bmp;
        }

        private static void RenderGrid(System.Drawing.Graphics g, Bitmap bmp, int mapSize, int mapRes, int imageWidth)
        {
            var penColor = System.Drawing.Color.FromArgb(120, 0, 0, 0);
            using var pen = new Pen(penColor, 1f);

            float cellPixels = (float)mapRes / (mapSize / GridCellSize);
            int cellCount = (int)(mapSize / GridCellSize);

            for (int i = 0; i <= cellCount; i++)
            {
                float x = OceanMargin + i * cellPixels;
                if (x >= OceanMargin && x <= imageWidth - OceanMargin)
                    g.DrawLine(pen, x, OceanMargin, x, imageWidth - OceanMargin);
            }
            for (int j = 0; j <= cellCount; j++)
            {
                float y = OceanMargin + j * cellPixels;
                if (y >= OceanMargin && y <= imageWidth - OceanMargin)
                    g.DrawLine(pen, OceanMargin, y, imageWidth - OceanMargin, y);
            }

            using var brush = new SolidBrush(penColor);
            using var font = new Font("Arial", 12f, FontStyle.Bold);
            float pad = 5f;

            for (int k = 0; k < cellCount; k++)
            {
                for (int l = 0; l < cellCount; l++)
                {
                    float x1 = OceanMargin + k * cellPixels;
                    float y1 = OceanMargin + l * cellPixels;
                    float x2 = x1 + cellPixels;
                    float y2 = y1 + cellPixels;
                    bool inBounds = x1 >= OceanMargin && x2 <= imageWidth - OceanMargin &&
                                    y1 >= OceanMargin && y2 <= imageWidth - OceanMargin;
                    bool edge = x1 >= OceanMargin && x1 <= imageWidth - OceanMargin &&
                               y1 >= OceanMargin && y1 <= imageWidth - OceanMargin && x2 > imageWidth - OceanMargin;

                    if (inBounds || edge)
                    {
                        // Rust grid: row 1 at top (North), letters A at left (West). Game PNG has top=North.
                        string label = k <= 25
                            ? $"{(char)(65 + k)}{l + 1}"
                            : $"{(char)(65 + (k / 26 - 1))}{(char)(65 + k % 26)}{l + 1}";
                        float dx = x1 + pad;
                        float dy = y1 + pad;
                        g.DrawString(label, font, brush, dx, dy);
                    }
                }
            }
        }

        private static void RenderMonumentLabels(System.Drawing.Graphics g, Bitmap bmp, int mapSize, int mapRes, int imageWidth)
        {
            var path = TerrainMeta.Path;
            if (path == null) return;

            var monuments = TerrainPathAccess.GetMonuments(path);
            if (monuments == null || monuments.Count == 0) return;

            Font font = null;
            foreach (var folder in new[] { FontFolder, "mapimages/resources" })
            {
                string fontPath = Path.Combine(Environment.CurrentDirectory, folder, MonumentFont);
                if (File.Exists(fontPath))
                {
                    try
                    {
                        var pfc = new PrivateFontCollection();
                        pfc.AddFontFile(fontPath);
                        font = new Font(pfc.Families[0], MonumentFontSize);
                        break;
                    }
                    catch { }
                }
            }
            if (font == null)
            {
                foreach (var fontName in FallbackFontNames)
                {
                    try
                    {
                        font = new Font(fontName, MonumentFontSize * 0.85f, FontStyle.Bold);
                        break;
                    }
                    catch { }
                }
                if (font == null) font = new Font("Arial", MonumentFontSize * 0.7f, FontStyle.Bold);
            }

            using (font)
            using (var brush = new SolidBrush(System.Drawing.Color.Black))
            {
                int offset = OceanMargin;

                foreach (MonumentInfo monument in monuments)
                {
                    if (monument == null) continue;
                    string name = GetMonumentName(monument);
                    if (string.IsNullOrEmpty(name)) continue;

                    // HarmonyCustomGenerator: show label when (shouldDisplayOnMap && mapIcon==null) OR train
                    if (monument.mapIcon != null && !name.ToLowerInvariant().Contains("train")) continue;
                    if (!monument.shouldDisplayOnMap && !name.ToLowerInvariant().Contains("train")) continue;

                    var pos = monument.transform.position;
                    // Use TerrainMeta normalized coords - game PNG has north at top, so normZ 0=south 1=north
                    float normX = TerrainMeta.NormalizeX(pos.x);
                    float normZ = TerrainMeta.NormalizeZ(pos.z);
                    int px = (int)(normX * mapRes) + offset;
                    int py = (int)((1f - normZ) * mapRes) + offset;  // flip Y: north at top of bitmap

                    // Oil rigs are usually offshore off the map edge - clamp inside footprint so they show
                    if (name.ToLowerInvariant().Contains("oil rig") || name.ToLowerInvariant().Contains("oilrig") ||
                        (monument.name != null && monument.name.ToLowerInvariant().Contains("oil_rig")))
                    {
                        px = Mathf.Clamp(px, offset, mapRes + offset);
                        py = Mathf.Clamp(py, offset, mapRes + offset);
                    }

                    var size = g.MeasureString(name, font);
                    float dx = px - size.Width / 2f;
                    float dy = py - size.Height / 2f;
                    g.DrawString(name, font, brush, dx, dy);
                }
            }
        }

        private static string GetMonumentName(MonumentInfo monument)
        {
            if (monument == null) return null;
            try
            {
                var phraseProp = monument.GetType().GetProperty("displayPhrase", BindingFlags.Public | BindingFlags.Instance);
                var phrase = phraseProp?.GetValue(monument);
                if (phrase != null)
                {
                    var isValidMethod = phrase.GetType().GetMethod("IsValid", Type.EmptyTypes);
                    if (true.Equals(isValidMethod?.Invoke(phrase, null)))
                    {
                        var enProp = phrase.GetType().GetProperty("english", BindingFlags.Public | BindingFlags.Instance);
                        var en = enProp?.GetValue(phrase) as string;
                        if (!string.IsNullOrEmpty(en))
                            return en.Replace("\n", "");
                    }
                }
                if (monument.Type == MonumentType.Cave) return "Cave";
                if (!string.IsNullOrEmpty(monument.name) && monument.name.Contains("power_sub")) return "Power Sub Station";
                // Fallback: displayPhrase often invalid during procgen - parse prefab path to friendly name
                return PrefabPathToDisplayName(monument.name) ?? monument.GetType().Name;
            }
            catch { return monument?.GetType().Name; }
        }

        /// <summary>
        /// Converts prefab path (e.g. assets/prefabs/world/monuments/gas_station.prefab) to display name (Gas Station).
        /// </summary>
        private static string PrefabPathToDisplayName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string name = path;
            int slash = name.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0) name = name.Substring(slash + 1);
            name = name.Replace("(Clone)", "").Replace(".prefab", "").Trim();
            if (string.IsNullOrEmpty(name)) return null;
            // Underscores to spaces, then title case each word (e.g. gas_station -> Gas Station)
            string[] parts = name.Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + (parts[i].Length > 1 ? parts[i].Substring(1).ToLowerInvariant() : "");
            }
            return string.Join(" ", parts);
        }
    }
}
