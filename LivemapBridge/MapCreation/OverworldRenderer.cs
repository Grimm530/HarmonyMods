using System.Threading.Tasks;
using UnityEngine;

namespace LivemapBridge.MapCreation;

/// <summary>
/// Headless overworld paint for the web map. Same TerrainMeta sampling and
/// OceanMargin UV math as Minimap's MapRenderer.RenderOverworld, without UI,
/// markers, fog, or a Minimap install.
/// </summary>
public static class OverworldRenderer
{
    public const int OceanMargin = 500;

    static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(0.95f, 2.87f, 2.37f));
    const float SunPower = 0.65f;
    const float Brightness = 1.05f;
    const float Contrast = 0.94f;
    static readonly Color Overlay = new Color(0.15f, 0.15f, 0.15f, 0.25f);
    static readonly Color Half = new Color(0.5f, 0.5f, 0.5f, 1f);

    // Minimap MapConfig Overworld defaults (SplatColor enum order).
    static readonly Color[] Splat =
    {
        new Color(0.28627452f, 23f / 85f, 0.24705884f), // Base
        new Color(0.25f, 37f / 152f, 0.22039475f), // Gravel
        new Color(0.6f, 0.47959462f, 0.33f), // Dirt
        new Color(0.7f, 0.65968585f, 0.5277487f), // Sand
        new Color(0.35486364f, 0.37f, 0.2035f), // Grass
        new Color(0.24843751f, 0.3f, 9f / 128f), // Forest
        new Color(0.4f, 0.39379844f, 0.37519377f), // Rock
        new Color(0.86274517f, 0.9294118f, 0.94117653f), // Snow
        new Color(7f / 51f, 0.2784314f, 0.2761563f), // Pebble
        new Color(0.16941601f, 0.31755757f, 0.36200002f), // Water
        new Color(0.04090196f, 0.22060032f, 14f / 51f) // Offshore
    };

    public static int ClampResolution(int requested)
    {
        int n = Mathf.Clamp(requested <= 0 ? 2048 : requested, 1024, 4096);
        if (n <= 1536) return 1024;
        if (n <= 3072) return 2048;
        return 4096;
    }

    public static bool TerrainReady()
    {
        return TerrainMeta.Size.x > 0f
               && TerrainMeta.HeightMap != null
               && TerrainMeta.SplatMap != null;
    }

    public static byte[] RenderPng(int requestedResolution, out int renderRes)
    {
        renderRes = ClampResolution(requestedResolution);
        if (!TerrainReady())
            return null;

        TerrainHeightMap heights = TerrainMeta.HeightMap;
        TerrainSplatMap splat = TerrainMeta.SplatMap;
        TerrainTopologyMap topology = TerrainMeta.TopologyMap;
        TerrainTexturing texturing = TerrainMeta.Texturing;
        float world = TerrainMeta.Size.x;
        float scaledMargin = OceanMargin / (world + 2f * OceanMargin) * renderRes;
        float invImageRes = 1f / (renderRes - scaledMargin * 2f);
        float maxDepth = Mathf.Max(Mathf.Abs(heights.GetHeight(0, 0)), 5f);
        int res = renderRes;
        var pixels = new Color[res * res];

        Parallel.For(0, res, y =>
        {
            float normZ = (y - scaledMargin) * invImageRes;
            for (int x = 0; x < res; x++)
            {
                float normX = (x - scaledMargin) * invImageRes;
                Color pixel = SampleMainland(normX, normZ, heights, splat, topology, texturing, maxDepth);
                pixels[y * res + x] = BlendOverlay(pixel, Overlay);
            }
        });

        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(pixels);
        tex.Apply(false, false);
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        return png;
    }

    public static byte[] RenderHeightBin(int outRes = 513)
    {
        TerrainHeightMap hm = TerrainMeta.HeightMap;
        if (hm == null || outRes < 2)
            return null;
        var buf = new byte[outRes * outRes * 2];
        float div = outRes - 1;
        for (int z = 0; z < outRes; z++)
        {
            float v = z / div;
            for (int x = 0; x < outRes; x++)
            {
                float u = x / div;
                float h01 = Mathf.Clamp01(hm.GetHeight01(u, v));
                ushort packed = (ushort)Mathf.RoundToInt(h01 * 65535f);
                int i = (z * outRes + x) * 2;
                buf[i] = (byte)(packed & 0xff);
                buf[i + 1] = (byte)(packed >> 8);
            }
        }
        return buf;
    }

    static Color SampleMainland(float normX, float normZ, TerrainHeightMap heights, TerrainSplatMap splat,
        TerrainTopologyMap topology, TerrainTexturing texturing, float maxDepth)
    {
        bool inMap = normX >= 0f && normX <= 1f && normZ >= 0f && normZ <= 1f;
        if (!inMap)
            return Splat[10];

        Color start = Splat[0];
        float height = heights.GetHeight(normX, normZ);
        Vector3 normal = heights.GetNormal(normX, normZ);
        float shoreDist = 0f;
        if (texturing != null)
            shoreDist = texturing.GetMainlandCoarseVectorToShore(normX, normZ).shoreDist;
        bool waterTopo = topology != null && (topology.GetTopology(normX, normZ, 16f) & 0x180) != 0;

        start = Color.Lerp(start, Splat[1], splat.GetSplat(normX, normZ, 128));
        start = Color.Lerp(start, Splat[8], splat.GetSplat(normX, normZ, 64));
        start = Color.Lerp(start, Splat[6], splat.GetSplat(normX, normZ, 8));
        start = Color.Lerp(start, Splat[2], splat.GetSplat(normX, normZ, 1));
        start = Color.Lerp(start, Splat[4], splat.GetSplat(normX, normZ, 16));
        start = Color.Lerp(start, Splat[5], splat.GetSplat(normX, normZ, 32));
        start = Color.Lerp(start, Splat[3], splat.GetSplat(normX, normZ, 4));
        start = Color.Lerp(start, Splat[7], splat.GetSplat(normX, normZ, 2));

        float waterDepth = 0f;
        if (shoreDist > 0f)
        {
            waterDepth = 0f - height;
            if (waterDepth <= 0f || !waterTopo)
                waterDepth = Mathf.Max(waterDepth, 0.1f * shoreDist);
        }

        if (waterDepth > 0f)
        {
            start = Color.Lerp(start, Splat[9], Mathf.Clamp(0.5f + waterDepth / 5f, 0f, 1f));
            start = Color.Lerp(start, Splat[10], Mathf.Clamp(waterDepth / maxDepth, 0f, 1f));
        }
        else
        {
            float sun = Mathf.Max(Vector3.Dot(normal, SunDirection), 0f);
            start += (sun - 0.5f) * SunPower * start;
            start = (start - Half) * Contrast + Half;
        }

        start *= Brightness;
        start.a = 1f;
        return start;
    }

    static Color BlendOverlay(Color original, Color overlay)
    {
        float a = Mathf.Clamp01(overlay.a);
        return new Color(
            overlay.r * a + original.r * (1f - a),
            overlay.g * a + original.g * (1f - a),
            overlay.b * a + original.b * (1f - a),
            1f);
    }
}
