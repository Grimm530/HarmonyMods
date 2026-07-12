using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Utility
{
    /// <summary>
    /// Full map image renderer - terrain + monuments in one pass (HarmonyCustomGenerator-style).
    /// Renders in memory without PNG load/save cycle for maximum quality.
    /// Fonts: place dinprobold.otf, dinpro.otf, or PermanentMarker.ttf in maps/images/resources or mapimages/resources (no remote download).
    /// </summary>
    public static class MapImageRenderFull
    {
        private static readonly string[] DefaultFontFolders = { "maps/images/resources", "mapimages/resources" };
        private static readonly string[] MonumentFontOrder = { "dinprobold.otf", "dinpro.otf", "PermanentMarker.ttf" };

        private static readonly Vector4 StartColor = new Vector4(0.28627452f, 23f / 85f, 0.24705884f, 1f);
        private static readonly Vector4 WaterColor = new Vector4(0.16941601f, 0.31755757f, 0.36200002f, 1f);
        private static readonly Vector4 GravelColor = new Vector4(0.25f, 37f / 152f, 0.22039475f, 1f);
        private static readonly Vector4 DirtColor = new Vector4(0.6f, 0.47959462f, 0.33f, 1f);
        private static readonly Vector4 SandColor = new Vector4(0.7f, 0.65968585f, 0.5277487f, 1f);
        private static readonly Vector4 GrassColor = new Vector4(0.35486364f, 0.37f, 0.2035f, 1f);
        private static readonly Vector4 ForestColor = new Vector4(0.24843751f, 0.3f, 9f / 128f, 1f);
        private static readonly Vector4 RockColor = new Vector4(0.4f, 0.39379844f, 0.37519377f, 1f);
        private static readonly Vector4 SnowColor = new Vector4(0.86274517f, 0.9294118f, 0.94117653f, 1f);
        private static readonly Vector4 PebbleColor = new Vector4(7f / 51f, 0.2784314f, 0.2761563f, 1f);
        private static readonly Vector4 OffShoreColor = new Vector4(0.04090196f, 0.22060032f, 14f / 51f, 1f);
        private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(0.95f, 2.87f, 2.37f));
        private static readonly Vector4 Half = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
        private static readonly float GridCellSize = 146.3f;

        public static byte[] Render(out int imageWidth, out int imageHeight, out UnityEngine.Color background,
            float scale, bool includeMonumentNames, bool includeGrid, int oceanMargin = 150, string fontResourcesPath = null, string preferredFont = null)
        {
            imageWidth = 0;
            imageHeight = 0;
            background = new UnityEngine.Color(OffShoreColor.x, OffShoreColor.y, OffShoreColor.z);

            UnityEngine.Debug.Log("[CustomMapGen] 0/6 | Starting rendering map...");
            if (TerrainTexturing.Instance == null) return null;
            var terrainHeightMap = TerrainTexturing.Instance.GetComponent<TerrainHeightMap>();
            var terrainSplatMap = TerrainTexturing.Instance.GetComponent<TerrainSplatMap>();
            var terrainTopologyMap = TerrainTexturing.Instance.GetComponent<TerrainTopologyMap>();
            if (terrainHeightMap == null || terrainSplatMap == null || terrainTopologyMap == null) return null;

            int mapRes = (int)(World.Size * Mathf.Clamp(scale, 0.1f, 4f));
            if (mapRes <= 0) return null;

            int margin = Mathf.Clamp(oceanMargin, 100, 500);
            if (mapRes + margin * 2 > 4096)
            {
                float factor = 4096f / (mapRes + margin * 2);
                mapRes = (int)(mapRes * factor);
                margin = (int)(margin * factor);
            }
            float invMapRes = 1f / mapRes;
            imageWidth = mapRes + margin * 2;
            imageHeight = mapRes + margin * 2;
            var array = new UnityEngine.Color[imageWidth * imageHeight];
            var output = new MapOutput(array, imageWidth, imageHeight);

            UnityEngine.Debug.Log("[CustomMapGen] 1/6 | Render begin...");
            float maxDepth = 50f;
            Parallel.For(0, imageHeight, y =>
            {
                int py = y - margin;
                float y2 = py * invMapRes;
                for (int px = -margin; px < mapRes + margin; px++)
                {
                    float x2 = px * invMapRes;
                    Vector4 startColor = StartColor;

                    float h = terrainHeightMap.GetHeight(x2, y2);
                    Vector3 n = terrainHeightMap.GetNormal(x2, y2);
                    float shoreDist = TerrainTexturing.Instance.GetMainlandCoarseVectorToShore(x2, y2).shoreDist;
                    bool shoreTopo = (terrainTopologyMap.GetTopology(x2, y2, 16f) & 0x180) != 0;
                    float sunDot = Math.Max(Vector3.Dot(n, SunDirection), 0f);

                    startColor = Vector4.Lerp(startColor, GravelColor, terrainSplatMap.GetSplat(x2, y2, 128) * GravelColor.w);
                    startColor = Vector4.Lerp(startColor, PebbleColor, terrainSplatMap.GetSplat(x2, y2, 64) * PebbleColor.w);
                    startColor = Vector4.Lerp(startColor, RockColor, terrainSplatMap.GetSplat(x2, y2, 8) * RockColor.w);
                    startColor = Vector4.Lerp(startColor, DirtColor, terrainSplatMap.GetSplat(x2, y2, 1) * DirtColor.w);
                    startColor = Vector4.Lerp(startColor, GrassColor, terrainSplatMap.GetSplat(x2, y2, 16) * GrassColor.w);
                    startColor = Vector4.Lerp(startColor, ForestColor, terrainSplatMap.GetSplat(x2, y2, 32) * ForestColor.w);
                    startColor = Vector4.Lerp(startColor, SandColor, terrainSplatMap.GetSplat(x2, y2, 4) * SandColor.w);
                    startColor = Vector4.Lerp(startColor, SnowColor, terrainSplatMap.GetSplat(x2, y2, 2) * SnowColor.w);

                    float waterContrib = 0f;
                    if (shoreDist > 0f)
                    {
                        waterContrib = -h;
                        if (waterContrib <= 0f || !shoreTopo)
                            waterContrib = Mathf.Max(waterContrib, 0.1f * shoreDist);
                    }
                    if (waterContrib > 0f)
                    {
                        startColor = Vector4.Lerp(startColor, WaterColor, Mathf.Clamp(0.5f + waterContrib / 5f, 0f, 1f));
                        startColor = Vector4.Lerp(startColor, OffShoreColor, Mathf.Clamp(waterContrib / maxDepth, 0f, 1f));
                    }
                    else
                    {
                        startColor += (sunDot - 0.5f) * 0.65f * startColor;
                        startColor = (startColor - Half) * 0.94f + Half;
                    }
                    startColor *= 1.05f;

                    int ox = px + margin;
                    int oy = y;
                    output[ox, oy] = new UnityEngine.Color(startColor.x, startColor.y, startColor.z);
                }
            });

            background = output[0, 0];

            if (includeMonumentNames)
                RenderMonuments(ref output, mapRes, imageWidth, margin, fontResourcesPath, preferredFont);

            if (includeGrid)
            {
                UnityEngine.Debug.Log("[CustomMapGen] 4/6 | Rendering grid...");
                RenderGrid(ref output, mapRes, imageWidth, margin);
            }

            UnityEngine.Debug.Log("[CustomMapGen] 6/6 | Done! Encoding...");
            return EncodeToPng(imageWidth, imageHeight, array);
        }

        /// <summary>Creates font resource directories if needed. No remote download - fonts must exist locally.</summary>
        public static void EnsureFontDirs(string customFontPath = null)
        {
            if (!string.IsNullOrEmpty(customFontPath))
            {
                var dir = Path.IsPathRooted(customFontPath) ? customFontPath : Path.Combine(Environment.CurrentDirectory, customFontPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return;
            }
            foreach (var folder in DefaultFontFolders)
            {
                var dir = Path.Combine(Environment.CurrentDirectory, folder);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
        }

        /// <summary>Resolves monument font path. preferredFont: dinprobold, dinpro, or PermanentMarker.</summary>
        private static string ResolveMonumentFont(string customFontPath = null, string preferredFont = null)
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

        /// <summary>Projects fishing village markers from water (e.g. underwater lab) toward shore so they appear on land.</summary>
        private static Vector3 SnapFishingVillageToShore(Vector3 position, float mapSize)
        {
            if (TerrainMeta.HeightMap == null) return position;
            Vector3 samplePos = new Vector3(position.x, 0f, position.z);
            float terrainY = TerrainMeta.HeightMap.GetHeight(samplePos);
            float waterY = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(samplePos) : 0f;
            if (terrainY > waterY - 0.1f) return position;
            float step = 40f;
            int maxSteps = 25;
            Vector3 toCenter = new Vector3(-position.x, 0f, -position.z);
            if (toCenter.sqrMagnitude < 1f) return position;
            toCenter.Normalize();
            for (int i = 0; i < maxSteps; i++)
            {
                position += toCenter * step;
                samplePos.Set(position.x, 0f, position.z);
                if (Mathf.Abs(position.x) > mapSize * 0.6f || Mathf.Abs(position.z) > mapSize * 0.6f) break;
                terrainY = TerrainMeta.HeightMap.GetHeight(samplePos);
                waterY = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(samplePos) : 0f;
                if (terrainY > waterY - 0.1f) return position;
            }
            return position;
        }

        private static void RenderMonuments(ref MapOutput output, int mapRes, int imageWidth, int oceanMargin, string fontResourcesPath = null, string preferredFont = null)
        {
            UnityEngine.Debug.Log("[CustomMapGen] 3/4 | Proceeding map data...");
            var path = TerrainMeta.Path;
            var monumentsRender = TerrainPathAccess.GetMonuments(path);
            if (monumentsRender == null || monumentsRender.Count == 0) return;

            string fontPath = ResolveMonumentFont(fontResourcesPath, preferredFont);
            if (fontPath == null)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] No monument font found. Place dinprobold.otf, dinpro.otf, or PermanentMarker.ttf in maps/images/resources (or " + (fontResourcesPath ?? "default") + "). CurrentDir=" + Environment.CurrentDirectory);
                return;
            }
            UnityEngine.Debug.Log($"[CustomMapGen] 3/4 | Rendering monuments... (font: {Path.GetFileName(fontPath)} path={fontPath})");
            int offset = imageWidth - mapRes - oceanMargin;
            float mapSize = (float)World.Size;

            var list = new List<(string name, int x, int y)>();
            foreach (MonumentInfo m in monumentsRender)
            {
                if (m == null) continue;
                string name = GetMonumentName(m);
                if (string.IsNullOrEmpty(name)) continue;
                if (m.mapIcon != null && !name.ToLowerInvariant().Contains("train")) continue;
                if (!m.shouldDisplayOnMap && !name.ToLowerInvariant().Contains("train")) continue;

                var pos = m.transform.position;
                string prefabLower = m.name?.ToLowerInvariant() ?? "";
                bool isFishingVillage = name.ToLowerInvariant().Contains("fishing village");
                if (isFishingVillage && (prefabLower.Contains("underwater_lab") || prefabLower.Contains("fishing_village")))
                {
                    pos = SnapFishingVillageToShore(pos, mapSize);
                }
                int x = (int)((pos.x + mapSize / 2) / mapSize * mapRes) + offset;
                int y = (int)((pos.z + mapSize / 2) / mapSize * mapRes) + offset;
                if (name.ToLowerInvariant().Contains("oil rig") || name.ToLowerInvariant().Contains("oilrig") || prefabLower.Contains("oil_rig"))
                {
                    x = Mathf.Clamp(x, oceanMargin, mapRes + oceanMargin);
                    y = Mathf.Clamp(y, oceanMargin, mapRes + oceanMargin);
                }
                list.Add((name, x, y));
            }

            foreach (var (name, x, y) in list)
                RenderText(name, fontPath, 22, System.Drawing.Color.Black, ref output, x, y);
        }

        private static void RenderText(string text, string fontPath, int fontSize, System.Drawing.Color color, ref MapOutput output, int xx, int yy)
        {
            string absPath = Path.GetFullPath(fontPath);
            if (!File.Exists(absPath))
                return;
            using (var bmp = output.ToBitmap())
            using (var pfc = new PrivateFontCollection())
            {
                try
                {
                    pfc.AddFontFile(absPath);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[CustomMapGen] Failed to load font {Path.GetFileName(absPath)}: {ex.Message}");
                    return;
                }
                if (pfc.Families == null || pfc.Families.Length == 0)
                {
                    UnityEngine.Debug.LogWarning($"[CustomMapGen] Font file has no families: {Path.GetFileName(absPath)}");
                    return;
                }
                bool useBold = absPath.IndexOf("dinprobold", StringComparison.OrdinalIgnoreCase) >= 0;
                var style = useBold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
                using (var font = new Font(pfc.Families[0], fontSize, style))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(color))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var size = g.MeasureString(text, font);
                    float dx = xx - size.Width / 2f;
                    float dy = yy - size.Height / 2f;
                    g.TranslateTransform(dx, dy);
                    g.RotateTransform(180f);
                    g.ScaleTransform(-1f, 1f);
                    g.DrawString(text, font, brush, 0f, -size.Height);
                }
                output.FromBitmap(bmp);
            }
        }

        private static void RenderGrid(ref MapOutput output, int mapRes, int imageWidth, int oceanMargin)
        {
            float mapSize = (float)World.Size;
            float cellPixels = mapRes / (mapSize / GridCellSize);
            int cellCount = (int)(mapSize / GridCellSize);
            var penColor = System.Drawing.Color.FromArgb(120, 0, 0, 0);

            using (var bmp = output.ToBitmap())
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(penColor, 1f))
                {
                    for (int i = 0; i <= cellCount; i++)
                    {
                        float x = oceanMargin + i * cellPixels;
                        if (x >= oceanMargin && x <= imageWidth - oceanMargin)
                            g.DrawLine(pen, x, oceanMargin, x, imageWidth - oceanMargin);
                    }
                    for (int j = 0; j <= cellCount; j++)
                    {
                        float y = oceanMargin + j * cellPixels;
                        if (y >= oceanMargin && y <= imageWidth - oceanMargin)
                            g.DrawLine(pen, oceanMargin, y, imageWidth - oceanMargin, y);
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
                            float x1 = oceanMargin + k * cellPixels;
                            float y1 = oceanMargin + l * cellPixels;
                            float x2 = x1 + cellPixels;
                            float y2 = y1 + cellPixels;
                            if (x1 < oceanMargin || y1 < oceanMargin || x2 > imageWidth - oceanMargin || y2 > imageWidth - oceanMargin)
                                continue;
                            string label = k <= 25 ? $"{(char)(65 + k)}{cellCount - l}" : $"{(char)(65 + (k / 26 - 1))}{(char)(65 + k % 26)}{cellCount - l}";
                            float dx = x1 + pad;
                            float dy = y1 + pad + (cellPixels - pad * 6f);
                            g.TranslateTransform(dx, dy);
                            g.RotateTransform(180f);
                            g.ScaleTransform(-1f, 1f);
                            g.DrawString(label, font, brush, 0f, -font.Height);
                            g.ResetTransform();
                        }
                    }
                }
                output.FromBitmap(bmp);
            }
        }

        private static byte[] EncodeToPng(int width, int height, UnityEngine.Color[] pixels)
        {
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.SetPixels(pixels);
                tex.Apply();
                return ImageConversion.EncodeToPNG(tex);
            }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
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
                        if (!string.IsNullOrEmpty(en)) return en.Replace("\n", "");
                    }
                }
                if (monument.Type == MonumentType.Cave) return "Cave";
                if (!string.IsNullOrEmpty(monument.name) && monument.name.Contains("power_sub")) return "Power Sub Station";
                return PrefabPathToDisplayName(monument.name) ?? monument.GetType().Name;
            }
            catch { return monument?.GetType().Name; }
        }

        private static string PrefabPathToDisplayName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string name = path;
            int slash = name.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0) name = name.Substring(slash + 1);
            name = name.Replace("(Clone)", "").Replace(".prefab", "").Trim();
            if (string.IsNullOrEmpty(name)) return null;
            var parts = name.Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + (parts[i].Length > 1 ? parts[i].Substring(1).ToLowerInvariant() : "");
            }
            return string.Join(" ", parts);
        }

        private struct MapOutput
        {
            private readonly UnityEngine.Color[] _pixels;
            private readonly int _w, _h;

            public MapOutput(UnityEngine.Color[] pixels, int w, int h) { _pixels = pixels; _w = w; _h = h; }

            public ref UnityEngine.Color this[int x, int y]
            {
                get
                {
                    int i = Mathf.Clamp(y, 0, _h - 1) * _w + Mathf.Clamp(x, 0, _w - 1);
                    return ref _pixels[i];
                }
            }

            public Bitmap ToBitmap()
            {
                var bmp = new Bitmap(_w, _h);
                for (int i = 0; i < _h; i++)
                    for (int j = 0; j < _w; j++)
                        bmp.SetPixel(j, i, this[j, i].ToSystemDrawingColor());
                return bmp;
            }

            public void FromBitmap(Bitmap bmp)
            {
                for (int i = 0; i < _h; i++)
                    for (int j = 0; j < _w; j++)
                    {
                        var p = bmp.GetPixel(j, i);
                        this[j, i] = new UnityEngine.Color(p.R / 255f, p.G / 255f, p.B / 255f, p.A / 255f);
                    }
            }
        }
    }
}
