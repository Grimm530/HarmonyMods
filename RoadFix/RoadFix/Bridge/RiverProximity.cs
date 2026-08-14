using System.Collections.Generic;
using UnityEngine;

namespace RoadFix.Bridge;

/// <summary>
/// River proximity for road terrain. Must stay cheap: AdjustTerrainHeight calls the fade
/// callback per heightmap cell, so geometric river scans there will freeze procgen.
/// </summary>
internal static class RiverProximity
{
    private static byte[] _mask; // 0 = none, 1 = riverside, 2 = river
    private static int _res;
    private static bool _built;

    public static void EnsureCache()
    {
        if (_built && _mask != null)
            return;
        BuildCache();
    }

    public static void Invalidate()
    {
        _built = false;
        _mask = null;
        _res = 0;
    }

    /// <summary>
    /// 0 = on river (no terrain adjust), 0.5 = riverside, 1 = normal.
    /// O(1) after EnsureCache().
    /// </summary>
    public static float AdjustTerrainFade(float xn, float zn)
    {
        EnsureCache();
        int topology = TerrainMeta.TopologyMap.GetTopology(xn, zn);
        if ((topology & 0x4000) != 0)
            return 0f;
        if ((topology & 0x8000) != 0)
            return 0.5f;

        byte m = SampleMask(xn, zn);
        if (m >= 2)
            return 0f;
        if (m == 1)
            return 0.5f;
        return 1f;
    }

    public static bool IsOnRiver(Vector3 worldPos, float pad = 0f)
    {
        EnsureCache();
        float xn = TerrainMeta.NormalizeX(worldPos.x);
        float zn = TerrainMeta.NormalizeZ(worldPos.z);
        if ((TerrainMeta.TopologyMap.GetTopology(xn, zn) & 0xC000) != 0)
            return true;
        byte m = SampleMask(xn, zn);
        if (m >= 2)
            return true;
        // pad: treat nearby riverside as on-river for path elevation only
        return pad > 0f && m == 1;
    }

    /// <summary>
    /// True only inside the wet channel (vanilla GetRadius at that river distance).
    /// Does NOT include riverside topology / bank mask — use for bridge span detection
    /// so high banks don't inflate span length / stretch.
    /// </summary>
    public static bool IsInRiverChannel(Vector3 worldPos, float padMetres = 1.5f)
    {
        return TryGetChannelInfo(worldPos, padMetres, out _, out _, out _);
    }

    public static bool TryGetChannelInfo(
        Vector3 worldPos,
        float padMetres,
        out PathList river,
        out float riverDist,
        out float radius)
    {
        river = null;
        riverDist = 0f;
        radius = 0f;
        List<PathList> rivers = TerrainPathAccess.GetRivers(TerrainMeta.Path);
        if (rivers == null)
            return false;

        float best = float.MaxValue;
        PathList bestRiver = null;
        float bestD = 0f;

        foreach (PathList r in rivers)
        {
            if (r?.Path == null)
                continue;
            float step = Mathf.Max(3f, r.Path.Length / 120f);
            for (float d = 0f; d <= r.Path.Length; d += step)
            {
                Vector3 rp = r.Spline ? r.Path.GetPointCubicHermite(d) : r.Path.GetPoint(d);
                float dx = worldPos.x - rp.x;
                float dz = worldPos.z - rp.z;
                float distSq = dx * dx + dz * dz;
                if (distSq < best)
                {
                    best = distSq;
                    bestRiver = r;
                    bestD = d;
                }
            }
        }

        if (bestRiver == null)
            return false;

        // Refine around coarse hit so radius matches the real closest point.
        float refineBest = best;
        float refineD = bestD;
        for (float d = Mathf.Max(0f, bestD - 8f); d <= Mathf.Min(bestRiver.Path.Length, bestD + 8f); d += 1f)
        {
            Vector3 rp = bestRiver.Spline ? bestRiver.Path.GetPointCubicHermite(d) : bestRiver.Path.GetPoint(d);
            float dx = worldPos.x - rp.x;
            float dz = worldPos.z - rp.z;
            float distSq = dx * dx + dz * dz;
            if (distSq < refineBest)
            {
                refineBest = distSq;
                refineD = d;
            }
        }

        float rad = PathList.GetRadius(
            refineD,
            bestRiver.Path.Length,
            bestRiver.Width * 0.5f,
            bestRiver.RandomScale,
            scaleWidthWithLength: true);
        if (Mathf.Sqrt(refineBest) > rad + Mathf.Max(0f, padMetres))
            return false;

        river = bestRiver;
        riverDist = refineD;
        radius = rad;
        return true;
    }

    public static bool TryGetRiverBedY(Vector3 worldPos, float pad, out float bedY)
    {
        bedY = worldPos.y;
        List<PathList> rivers = TerrainPathAccess.GetRivers(TerrainMeta.Path);
        if (rivers == null)
            return false;

        float best = (24f + pad) * (24f + pad);
        bool found = false;
        foreach (PathList river in rivers)
        {
            if (river?.Path == null)
                continue;
            // Coarse step — only used for a few path points, not heightmap cells.
            float step = 8f;
            for (float d = 0f; d <= river.Path.Length; d += step)
            {
                Vector3 rp = river.Spline ? river.Path.GetPointCubicHermite(d) : river.Path.GetPoint(d);
                float dx = worldPos.x - rp.x;
                float dz = worldPos.z - rp.z;
                float distSq = dx * dx + dz * dz;
                if (distSq < best)
                {
                    best = distSq;
                    bedY = rp.y;
                    found = true;
                }
            }
        }
        return found;
    }

    private static byte SampleMask(float xn, float zn)
    {
        if (_mask == null || _res <= 0)
            return 0;
        int x = Mathf.Clamp(Mathf.FloorToInt(xn * _res), 0, _res - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(zn * _res), 0, _res - 1);
        return _mask[z * _res + x];
    }

    private static void BuildCache()
    {
        _built = true;
        _res = 256;
        _mask = new byte[_res * _res];
        List<PathList> rivers = TerrainPathAccess.GetRivers(TerrainMeta.Path);
        if (rivers == null)
            return;

        float mapSize = Mathf.Max(1f, TerrainMeta.Size.x);
        float cell = mapSize / _res;
        // River core ~ half width; riverside band ~ +12m (matches prior fade band).
        foreach (PathList river in rivers)
        {
            if (river?.Path == null)
                continue;
            float half = Mathf.Max(4f, river.Width * 0.5f);
            float bank = half + 12f;
            float step = Mathf.Max(4f, half * 0.5f);
            for (float d = 0f; d <= river.Path.Length; d += step)
            {
                Vector3 rp = river.Spline ? river.Path.GetPointCubicHermite(d) : river.Path.GetPoint(d);
                StampDisk(rp.x, rp.z, bank, cell, core: half);
            }
        }
    }

    private static void StampDisk(float worldX, float worldZ, float radius, float cell, float core)
    {
        float xn = TerrainMeta.NormalizeX(worldX);
        float zn = TerrainMeta.NormalizeZ(worldZ);
        int cx = Mathf.FloorToInt(xn * _res);
        int cz = Mathf.FloorToInt(zn * _res);
        int rCells = Mathf.CeilToInt(radius / cell) + 1;
        for (int dz = -rCells; dz <= rCells; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= _res)
                continue;
            for (int dx = -rCells; dx <= rCells; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= _res)
                    continue;
                float metres = Mathf.Sqrt(dx * dx + dz * dz) * cell;
                int idx = z * _res + x;
                if (metres <= core)
                    _mask[idx] = 2;
                else if (metres <= radius && _mask[idx] < 2)
                    _mask[idx] = 1;
            }
        }
    }
}
