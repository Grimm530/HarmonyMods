using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using UnityEngine;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using Graphics = System.Drawing.Graphics;

namespace CustomMapGen.Utility
{
    /// <summary>
    /// Paints cargo path and monument names onto the PNG from <see cref="MapImageRenderer"/>
    /// (same output as client <c>world.rendermap</c>). Does not re-render terrain.
    /// Game PNG is north-up: use NormalizeX and (1 - NormalizeZ).
    /// </summary>
    public static class MapImageOverlay
    {
        private const float GridCellSize = 146.3f;
        private static readonly string[] DefaultFontFolders = { "maps/images/resources", "mapimages/resources" };
        private static readonly string[] MonumentFontOrder = { "dinprobold.otf", "dinpro.otf", "PermanentMarker.ttf" };

        public static byte[] ApplyOverlays(byte[] basePng, int oceanMargin,
            bool includeMonumentNames, bool includeGrid, bool includeCargo,
            string fontResourcesPath, string preferredFont)
        {
            if (basePng == null || basePng.Length == 0)
                return basePng;
            if (!includeMonumentNames && !includeGrid && !includeCargo)
                return basePng;

            using var ms = new MemoryStream(basePng);
            using var bmp = new Bitmap(ms);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            int margin = Mathf.Clamp(oceanMargin, 0, bmp.Width / 2);
            int mapRes = bmp.Width - margin * 2;
            if (mapRes <= 0)
                return basePng;

            if (includeCargo)
                RenderCargoPath(g, mapRes, margin);

            if (includeGrid)
                RenderGrid(g, mapRes, bmp.Width, margin);

            if (includeMonumentNames)
                RenderMonumentLabels(g, mapRes, margin, fontResourcesPath, preferredFont);

            using var outMs = new MemoryStream();
            bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
            return outMs.ToArray();
        }

        static void WorldToPng(Vector3 world, int mapRes, int margin, out float x, out float y)
        {
            x = margin + TerrainMeta.NormalizeX(world.x) * mapRes;
            y = margin + (1f - TerrainMeta.NormalizeZ(world.z)) * mapRes;
        }

        static void RenderCargoPath(System.Drawing.Graphics g, int mapRes, int margin)
        {
            var points = TerrainPathAccess.GetOceanPatrolFar(TerrainMeta.Path);
            if (points == null || points.Count < 2)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] Cargo path skipped: OceanPatrolFar is empty.");
                return;
            }

            var simplified = SimplifyLoop(points, 90f);
            UnityEngine.Debug.Log($"[CustomMapGen] Painting cargo path ({simplified.Count} of {points.Count} Far nodes) on game map image.");
            var penColor = System.Drawing.Color.FromArgb(200, 220, 90, 20);
            using (var pen = new Pen(penColor, 3f))
            {
                var pts = new PointF[simplified.Count];
                for (int i = 0; i < simplified.Count; i++)
                {
                    WorldToPng(simplified[i], mapRes, margin, out float x, out float y);
                    pts[i] = new PointF(x, y);
                }
                g.DrawLines(pen, pts);
                float closeSq = (simplified[0] - simplified[simplified.Count - 1]).sqrMagnitude;
                float maxClose = World.Size * 0.12f;
                if (closeSq < maxClose * maxClose)
                    g.DrawLine(pen, pts[pts.Length - 1], pts[0]);
            }
        }

        static List<Vector3> SimplifyLoop(IList<Vector3> points, float minSep)
        {
            var result = new List<Vector3>(Math.Min(points.Count, 160));
            result.Add(points[0]);
            Vector3 last = points[0];
            float minSepSq = minSep * minSep;
            for (int i = 1; i < points.Count; i++)
            {
                if ((points[i] - last).sqrMagnitude < minSepSq)
                    continue;
                result.Add(points[i]);
                last = points[i];
            }
            return result.Count >= 2 ? result : new List<Vector3>(points);
        }

        static void RenderGrid(System.Drawing.Graphics g, int mapRes, int imageWidth, int margin)
        {
            float mapSize = (float)World.Size;
            float cellPixels = mapRes / (mapSize / GridCellSize);
            int cellCount = (int)(mapSize / GridCellSize);
            var penColor = System.Drawing.Color.FromArgb(120, 0, 0, 0);

            using (var pen = new Pen(penColor, 1f))
            {
                for (int i = 0; i <= cellCount; i++)
                {
                    float x = margin + i * cellPixels;
                    if (x >= margin && x <= imageWidth - margin)
                        g.DrawLine(pen, x, margin, x, imageWidth - margin);
                }
                for (int j = 0; j <= cellCount; j++)
                {
                    float y = margin + j * cellPixels;
                    if (y >= margin && y <= imageWidth - margin)
                        g.DrawLine(pen, margin, y, imageWidth - margin, y);
                }
            }
            using (var font = new Font("Arial", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(penColor))
            {
                float pad = 5f;
                for (int k = 0; k < cellCount; k++)
                {
                    for (int l = 0; l < cellCount; l++)
                    {
                        float x1 = margin + k * cellPixels;
                        float y1 = margin + l * cellPixels;
                        float x2 = x1 + cellPixels;
                        float y2 = y1 + cellPixels;
                        if (x1 < margin || y1 < margin || x2 > imageWidth - margin || y2 > imageWidth - margin)
                            continue;
                        string label = k <= 25
                            ? $"{(char)(65 + k)}{l + 1}"
                            : $"{(char)(65 + (k / 26 - 1))}{(char)(65 + k % 26)}{l + 1}";
                        g.DrawString(label, font, brush, x1 + pad, y1 + pad);
                    }
                }
            }
        }

        static void RenderMonumentLabels(System.Drawing.Graphics g, int mapRes, int margin,
            string fontResourcesPath, string preferredFont)
        {
            var monuments = TerrainPathAccess.GetMonuments(TerrainMeta.Path);
            if (monuments == null || monuments.Count == 0)
                return;

            string fontPath = ResolveMonumentFont(fontResourcesPath, preferredFont);
            PrivateFontCollection pfc = null;
            Font fallbackFont = null;
            try
            {
                FontFamily family = null;
                if (fontPath != null && File.Exists(fontPath))
                {
                    pfc = new PrivateFontCollection();
                    pfc.AddFontFile(Path.GetFullPath(fontPath));
                    if (pfc.Families != null && pfc.Families.Length > 0)
                        family = pfc.Families[0];
                }

                float imageScale = (mapRes + margin * 2) / 3300f;
                var fill = System.Drawing.Color.FromArgb(255, 250, 246, 232);
                var outline = System.Drawing.Color.FromArgb(220, 18, 18, 18);
                using (var fillBrush = new SolidBrush(fill))
                using (var outlineBrush = new SolidBrush(outline))
                {
                    var fonts = new Dictionary<int, Font>();
                    try
                    {
                        foreach (MonumentInfo monument in monuments)
                        {
                            if (monument == null || !MonumentMapLabels.TryGet(monument, out var label))
                                continue;

                            Vector3 pos = monument.transform.position;
                            WorldToPng(pos, mapRes, margin, out float fx, out float fy);
                            int px = (int)fx;
                            int py = (int)fy;
                            string prefabLower = monument.name?.ToLowerInvariant() ?? "";
                            if (label.Text.IndexOf("Oil Rig", StringComparison.OrdinalIgnoreCase) >= 0
                                || prefabLower.Contains("oil_rig") || prefabLower.Contains("oilrig"))
                            {
                                px = Mathf.Clamp(px, margin, mapRes + margin);
                                py = Mathf.Clamp(py, margin, mapRes + margin);
                            }

                            int fontSize = MonumentMapLabels.FontSize(label.Rank, imageScale);
                            if (!fonts.TryGetValue(fontSize, out var font))
                            {
                                if (family != null)
                                    font = new Font(family, fontSize, FontStyle.Bold);
                                else
                                {
                                    if (fallbackFont == null)
                                        fallbackFont = new Font("Arial", fontSize, FontStyle.Bold);
                                    font = new Font("Arial", fontSize, FontStyle.Bold);
                                }
                                fonts[fontSize] = font;
                            }

                            var size = g.MeasureString(label.Text, font);
                            float dx = px - size.Width / 2f;
                            float dy = py - size.Height / 2f;
                            const int halo = 2;
                            for (int ox = -halo; ox <= halo; ox++)
                            {
                                for (int oy = -halo; oy <= halo; oy++)
                                {
                                    if (ox == 0 && oy == 0)
                                        continue;
                                    g.DrawString(label.Text, font, outlineBrush, dx + ox, dy + oy);
                                }
                            }
                            g.DrawString(label.Text, font, fillBrush, dx, dy);
                        }
                    }
                    finally
                    {
                        foreach (var font in fonts.Values)
                            font.Dispose();
                    }
                }
            }
            finally
            {
                fallbackFont?.Dispose();
                pfc?.Dispose();
            }
        }

        static string ResolveMonumentFont(string customFontPath, string preferredFont)
        {
            string[] order = MonumentFontOrder;
            if (!string.IsNullOrWhiteSpace(preferredFont))
            {
                string p = preferredFont.Trim().ToLowerInvariant();
                string preferredFile = (p == "dinprobold") ? "dinprobold.otf" : (p == "dinpro") ? "dinpro.otf" : (p == "permanentmarker") ? "PermanentMarker.ttf" : null;
                if (preferredFile != null)
                {
                    var list = new List<string> { preferredFile };
                    foreach (var f in MonumentFontOrder)
                        if (f != preferredFile) list.Add(f);
                    order = list.ToArray();
                }
            }
            var baseDirs = new List<string> { Environment.CurrentDirectory };
            try
            {
                string dataParent = !string.IsNullOrEmpty(Application.dataPath) ? Path.GetDirectoryName(Application.dataPath) : null;
                if (!string.IsNullOrEmpty(dataParent) && !baseDirs.Contains(dataParent))
                    baseDirs.Add(dataParent);
            }
            catch { }

            if (!string.IsNullOrEmpty(customFontPath))
            {
                foreach (var baseDir in baseDirs)
                {
                    var dir = Path.IsPathRooted(customFontPath) ? customFontPath : Path.Combine(baseDir, customFontPath);
                    foreach (var fontFile in order)
                    {
                        var path = Path.Combine(dir, fontFile);
                        if (File.Exists(path)) return Path.GetFullPath(path);
                    }
                }
            }
            foreach (var baseDir in baseDirs)
            {
                foreach (var folder in DefaultFontFolders)
                {
                    foreach (var fontFile in order)
                    {
                        var path = Path.Combine(baseDir, folder, fontFile);
                        if (File.Exists(path)) return Path.GetFullPath(path);
                    }
                }
            }
            return null;
        }
    }
}
