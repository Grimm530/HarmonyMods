using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadFix.Bridge;

/// <summary>
/// Cuts road/rail fill at river crossings (lower-only).
/// Width = across channel (scaled GetRadius * widthScale).
/// Length = along river under the deck (LocalRiverSegmentPad) — must clear lips.
/// Depth = match natural bed beside the crossing (never scaled GetDepth pool bowls).
/// </summary>
internal static class RiverTerrainReapply
{
    public static void Reapply()
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null)
            return;

        if (cfg.FullRiverHeightReapply)
        {
            ReapplyFullRivers();
            return;
        }

        ReapplyAtCrossingsOnly();
    }

    private static void ReapplyFullRivers()
    {
        if (TerrainMeta.Path?.Rivers == null || TerrainMeta.Path.Rivers.Count == 0)
            return;

        TerrainHeightMap heightMap = TerrainMeta.HeightMap;
        int rivers = 0;
        foreach (PathList river in TerrainMeta.Path.Rivers.AsEnumerable().Reverse())
        {
            if (river?.Path == null)
                continue;
            heightMap.Push();
            river.AdjustTerrainHeight(1f, 1f, scaleWidthWithLength: true);
            heightMap.Pop();
            rivers++;
        }

        if (RoadFixConfig.Config?.DebugLogging == true)
            Debug.Log($"[RoadFix] Full river height re-apply on {rivers} river(s) (wide OuterFade — can float props)");
    }

    private static void ReapplyAtCrossingsOnly()
    {
        var crossings = new List<BridgeCrossing>();
        crossings.AddRange(BridgeService.GetRoadCrossings());
        crossings.AddRange(BridgeService.GetRailCrossings());
        if (crossings.Count == 0)
        {
            if (RoadFixConfig.Config?.DebugLogging == true)
                Debug.Log("[RoadFix] Local river carve: no crossings to carve");
            return;
        }

        var cfg = RoadFixConfig.Config;
        float widthScale = Mathf.Clamp(cfg.RiverCarveWidthScale, 0.25f, 1f);
        float alongHalfBase = Mathf.Max(12f, cfg.LocalRiverSegmentPad);
        float edgeFade = Mathf.Clamp(cfg.LocalRiverOuterFade, 1f, 4f);
        int carved = 0;
        int cells = 0;
        float maxWidthSeen = 0f;
        float maxAlongSeen = 0f;

        foreach (BridgeCrossing crossing in crossings)
        {
            if (crossing.Path?.Path == null)
                continue;
            if (!TryNearestRiverSample(crossing, out _, out PathList river, out float riverDist))
                continue;

            bool isRail = TerrainMeta.Path?.Rails != null && TerrainMeta.Path.Rails.Contains(crossing.Path);
            // Deck corridor (along-river / lateral to path): rails use wide bridgerail.map.
            float alongHalf = isRail
                ? Mathf.Max(alongHalfBase, 28f)
                : Mathf.Max(alongHalfBase, 18f);

            float fullRadius = PathList.GetRadius(
                riverDist, river.Path.Length, river.Width * 0.5f, river.RandomScale, scaleWidthWithLength: true);
            float widthHalf = fullRadius * widthScale;
            maxWidthSeen = Mathf.Max(maxWidthSeen, widthHalf);
            maxAlongSeen = Mathf.Max(maxAlongSeen, alongHalf);

            float bedY = SampleNaturalBedY(river, riverDist, alongHalf);
            cells += CarveCrossingChannel(crossing, river, riverDist, widthHalf, alongHalf, edgeFade, bedY);
            carved++;
        }

        if (cfg.DebugLogging)
        {
            Debug.Log(
                $"[RoadFix] Local LOWER-only carve at {carved}/{crossings.Count} crossing(s) " +
                $"(widthHalf≈{maxWidthSeen:F1}m scale={widthScale:F2} alongHalf≈{maxAlongSeen:F0}m " +
                $"edgeFade={edgeFade:F1}m cells≈{cells})");
        }
    }

    private static bool TryNearestRiverSample(
        BridgeCrossing crossing,
        out Vector3 riverPt,
        out PathList river,
        out float riverDist)
    {
        riverPt = default;
        river = null;
        riverDist = 0f;
        if (TerrainMeta.Path?.Rivers == null)
            return false;

        Vector3 center = crossing.Center;
        float best = float.MaxValue;
        foreach (PathList r in TerrainMeta.Path.Rivers)
        {
            if (r?.Path == null)
                continue;
            float step = Mathf.Max(2f, r.Path.Length / 100f);
            for (float d = 0f; d <= r.Path.Length; d += step)
            {
                Vector3 pt = r.Spline ? r.Path.GetPointCubicHermite(d) : r.Path.GetPoint(d);
                float dx = pt.x - center.x;
                float dz = pt.z - center.z;
                float distSq = dx * dx + dz * dz;
                if (distSq < best)
                {
                    best = distSq;
                    riverPt = pt;
                    river = r;
                    riverDist = d;
                }
            }
        }

        float maxDist = Mathf.Max(24f, crossing.SpanLength) + 16f;
        return river != null && best <= maxDist * maxDist;
    }

    /// <summary>
    /// Natural channel floor beside the crossing (outside the road fill), so we don't dig a pool.
    /// </summary>
    private static float SampleNaturalBedY(PathList river, float riverCenterDist, float alongHalf)
    {
        TerrainHeightMap heightMap = TerrainMeta.HeightMap;
        float offset = river.TerrainOffset != 0f ? river.TerrainOffset : -1.5f;
        // Unscaled shallow offset only — never GetDepth(scaleWidthWithLength) (up to 3× → pool).
        float shallowFloor = float.MaxValue;
        float sum = 0f;
        int n = 0;

        // Sample just past the carve along the river (true bed, not road fill under the deck).
        foreach (float sign in new[] { -1f, 1f })
        {
            for (float extra = 2f; extra <= 10f; extra += 2f)
            {
                float d = Mathf.Clamp(riverCenterDist + sign * (alongHalf + extra), 0f, river.Path.Length);
                Vector3 pt = BridgeTerrain.SamplePoint(river, d);
                float h = heightMap.GetHeight(pt);
                // Ignore samples that still look like raised embankment vs river path.
                if (h > pt.y + 3f)
                    continue;
                sum += h;
                n++;
                shallowFloor = Mathf.Min(shallowFloor, h);
            }
        }

        float fromPath = BridgeTerrain.SamplePoint(river, riverCenterDist).y + offset;
        if (n <= 0)
            return fromPath;

        float avg = sum / n;
        // Target the natural bed; never deeper than path+unscaled offset.
        return Mathf.Max(avg, fromPath);
    }

    private static int CarveCrossingChannel(
        BridgeCrossing crossing,
        PathList river,
        float riverCenterDist,
        float widthHalf,
        float alongHalf,
        float edgeFade,
        float bedY)
    {
        TerrainHeightMap heightMap = TerrainMeta.HeightMap;
        PathList road = crossing.Path;

        // Longer soft skirts on channel sides (avoids hard vertical walls).
        float sideFade = Mathf.Max(edgeFade, RoadFixConfig.Config?.LocalRiverInnerFade ?? 4f);
        float widthRadius = widthHalf + sideFade;
        float alongRadius = alongHalf + sideFade;

        // Long SmoothStep ramp bank→bed along the span (short linear fades = cliffs).
        float bankFade = Mathf.Clamp(crossing.SpanLength * 0.4f, 12f, 22f);
        float spanPad = bankFade * 0.5f;

        float searchR = Mathf.Max(widthRadius, alongRadius) + crossing.SpanLength * 0.5f + bankFade + 8f;
        Vector3 center = crossing.Center;
        center.y = 0f;

        float bed01 = TerrainMeta.NormalizeY(bedY);
        int cells = 0;

        heightMap.ForEach(center, searchR, (x, z) =>
        {
            float nx = heightMap.Coordinate(x);
            float nz = heightMap.Coordinate(z);
            Vector3 world = TerrainMeta.Denormalize(new Vector3(nx, 0f, nz));

            float riverD0 = Mathf.Max(0f, riverCenterDist - alongRadius);
            float riverD1 = Mathf.Min(river.Path.Length, riverCenterDist + alongRadius);
            if (!TryClosestOnPath(river, riverD0, riverD1, world, out float distRiver, out float riverY, out float riverD))
                return;

            if (distRiver > widthRadius)
                return;

            float along = Mathf.Abs(riverD - riverCenterDist);
            if (along > alongRadius)
                return;

            if (!TryClosestOnPath(
                    road,
                    crossing.StartDist - spanPad,
                    crossing.EndDist + spanPad,
                    world,
                    out float distRoad,
                    out _,
                    out float roadD))
                return;
            if (distRoad > alongRadius)
                return;

            // 1 at mid-span, 0 at / past banks — SmoothStep for a continuous slope.
            float endBlend = 1f;
            if (roadD <= crossing.StartDist)
                endBlend = 0f;
            else if (roadD >= crossing.EndDist)
                endBlend = 0f;
            else if (roadD < crossing.StartDist + bankFade)
                endBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(crossing.StartDist, crossing.StartDist + bankFade, roadD));
            else if (roadD > crossing.EndDist - bankFade)
                endBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(crossing.EndDist, crossing.EndDist - bankFade, roadD));
            if (endBlend <= 0.01f)
                return;

            float widthT = distRiver <= widthHalf
                ? 1f
                : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(widthHalf, widthRadius, distRiver));
            float alongT = along <= alongHalf
                ? 1f
                : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(alongHalf, alongRadius, along));
            float opacity = widthT * alongT;
            if (opacity <= 0.02f)
                return;

            float pathBed01 = TerrainMeta.NormalizeY(riverY + (river.TerrainOffset != 0f ? river.TerrainOffset : -1.5f));
            float midTarget01 = Mathf.Max(bed01, pathBed01);

            // Ramp target from approach path height (banks) down to bed (mid) — no hard shelf.
            float pathY = BridgeTerrain.SamplePoint(
                road,
                Mathf.Clamp(roadD, 0f, road.Path.Length)).y;
            float approach01 = TerrainMeta.NormalizeY(pathY);
            float localTarget01 = Mathf.Lerp(approach01, midTarget01, endBlend);

            float cur01 = heightMap.GetHeight01(x, z);
            if (cur01 <= localTarget01 + 0.0001f)
                return;

            heightMap.LowerHeight(x, z, localTarget01, opacity);
            cells++;
        });

        return cells;
    }

    private static bool TryClosestOnPath(
        PathList path,
        float d0,
        float d1,
        Vector3 world,
        out float distXZ,
        out float pathY,
        out float pathDist)
    {
        distXZ = float.MaxValue;
        pathY = 0f;
        pathDist = 0f;
        if (path?.Path == null)
            return false;

        d0 = Mathf.Clamp(d0, 0f, path.Path.Length);
        d1 = Mathf.Clamp(d1, 0f, path.Path.Length);
        if (d1 < d0)
            (d0, d1) = (d1, d0);

        float step = 2f;
        float best = float.MaxValue;
        float bestY = 0f;
        float bestD = d0;
        for (float d = d0; d <= d1; d += step)
        {
            Vector3 pt = BridgeTerrain.SamplePoint(path, d);
            float dx = world.x - pt.x;
            float dz = world.z - pt.z;
            float distSq = dx * dx + dz * dz;
            if (distSq < best)
            {
                best = distSq;
                bestY = pt.y;
                bestD = d;
            }
        }

        foreach (float d in new[] { d0, d1 })
        {
            Vector3 pt = BridgeTerrain.SamplePoint(path, d);
            float dx = world.x - pt.x;
            float dz = world.z - pt.z;
            float distSq = dx * dx + dz * dz;
            if (distSq < best)
            {
                best = distSq;
                bestY = pt.y;
                bestD = d;
            }
        }

        distXZ = Mathf.Sqrt(best);
        pathY = bestY;
        pathDist = bestD;
        return true;
    }
}
