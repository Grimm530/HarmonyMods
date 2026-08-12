using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadFix.Bridge;

/// <summary>
/// Cuts road/rail fill at river crossings (lower-only).
/// Samples consistent river-bed heights before and after the crossing, interpolates
/// that grade through the span, then smooths only the cells we touched.
/// </summary>
internal static class RiverTerrainReapply
{
    private struct BedAnchors
    {
        /// <summary>River distance at centre of before-sample window (grade start).</summary>
        public float BeforeDist;
        /// <summary>River distance at centre of after-sample window (grade end).</summary>
        public float AfterDist;
        /// <summary>Full carve corridor start/end along the river (includes sample stretches).</summary>
        public float Corridor0;
        public float Corridor1;
        public float BeforeY;
        public float AfterY;
        public float WidthHalf;
        public int BeforeCount;
        public int AfterCount;
    }

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
        float widthScale = Mathf.Clamp(cfg.RiverCarveWidthScale, 0.25f, 1.25f);
        int carved = 0;
        int cells = 0;
        float minBedSeen = float.MaxValue;

        foreach (BridgeCrossing crossing in crossings)
        {
            if (crossing.Path?.Path == null)
                continue;
            if (!TryNearestRiverSample(crossing, out _, out PathList river, out float riverDist))
                continue;

            if (!TrySampleBedAnchors(crossing, river, riverDist, widthScale, out BedAnchors anchors))
                continue;

            float bedMid = (anchors.BeforeY + anchors.AfterY) * 0.5f;
            minBedSeen = Mathf.Min(minBedSeen, bedMid);

            float alongHalf = (anchors.Corridor1 - anchors.Corridor0) * 0.5f;
            CrossingDiagnostics.LogCrossing(
                "pre-carve", crossing, river, riverDist, anchors.WidthHalf, alongHalf, bedMid);

            if (cfg.DebugLogging)
            {
                Debug.Log(
                    $"[RoadFix] bed-anchors '{crossing.Path.Name}' river d={riverDist:F1} " +
                    $"before d={anchors.BeforeDist:F1} Y={anchors.BeforeY:F2} n={anchors.BeforeCount} | " +
                    $"after d={anchors.AfterDist:F1} Y={anchors.AfterY:F2} n={anchors.AfterCount} | " +
                    $"corridor={anchors.Corridor0:F0}..{anchors.Corridor1:F0} " +
                    $"widthHalf={anchors.WidthHalf:F1} slope={(anchors.AfterY - anchors.BeforeY) / Mathf.Max(1f, anchors.AfterDist - anchors.BeforeDist):F3}");
            }

            cells += CarveThroughAnchors(crossing, river, anchors);
            carved++;
        }

        if (cfg.DebugLogging)
        {
            Debug.Log(
                $"[RoadFix] Before/after channel carve at {carved}/{crossings.Count} crossing(s) " +
                $"(minBedY≈{(minBedSeen < float.MaxValue * 0.5f ? minBedSeen : 0f):F2} " +
                $"bedBonus={cfg.BedDepthBonus:F2} cells≈{cells})");
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
    /// Read many centreline heights upstream and downstream of the road/rail influence,
    /// then take robust averages so the through-cut matches the existing river bed.
    /// </summary>
    private static bool TrySampleBedAnchors(
        BridgeCrossing crossing,
        PathList river,
        float riverDist,
        float widthScale,
        out BedAnchors anchors)
    {
        anchors = default;
        var cfg = RoadFixConfig.Config;
        float offset = river.TerrainOffset != 0f ? river.TerrainOffset : -1.5f;

        // Skip the raised road/rail fill at the crossing, then sample a stretch of clean bed.
        float gap = Mathf.Max(
            crossing.Path.Width * 0.6f + 6f,
            Mathf.Max(cfg.LocalRiverSegmentPad * 0.45f, 14f));
        float sampleLen = Mathf.Clamp(cfg.LocalRiverSegmentPad + 8f, 20f, 40f);
        float step = 2f;

        var before = new List<float>(24);
        var after = new List<float>(24);
        float widthSum = 0f;
        int widthN = 0;

        CollectSide(river, riverDist - gap, -1f, sampleLen, step, offset, before, ref widthSum, ref widthN);
        CollectSide(river, riverDist + gap, +1f, sampleLen, step, offset, after, ref widthSum, ref widthN);

        if (before.Count < 3 || after.Count < 3)
            return false;

        float beforeY = RobustAverage(before);
        float afterY = RobustAverage(after);

        float bonus = cfg.BedDepthBonus;
        // Tiny depth nudge only — profile comes from the real bed samples.
        float depthScale = Mathf.Clamp01(((widthSum / Mathf.Max(1, widthN)) - 3f) / 14f);
        beforeY += bonus * Mathf.Lerp(0.15f, 0.6f, depthScale);
        afterY += bonus * Mathf.Lerp(0.15f, 0.6f, depthScale);

        float avgRadius = widthN > 0 ? widthSum / widthN : river.Width * 0.5f;

        float beforeMid = Mathf.Max(0f, riverDist - gap - sampleLen * 0.5f);
        float afterMid = Mathf.Min(river.Path.Length, riverDist + gap + sampleLen * 0.5f);
        float corridor0 = Mathf.Max(0f, riverDist - gap - sampleLen);
        float corridor1 = Mathf.Min(river.Path.Length, riverDist + gap + sampleLen);

        anchors = new BedAnchors
        {
            BeforeDist = beforeMid,
            AfterDist = afterMid,
            Corridor0 = corridor0,
            Corridor1 = corridor1,
            BeforeY = beforeY,
            AfterY = afterY,
            WidthHalf = avgRadius * widthScale,
            BeforeCount = before.Count,
            AfterCount = after.Count
        };

        return anchors.AfterDist > anchors.BeforeDist + 4f && anchors.WidthHalf > 1.5f;
    }

    private static void CollectSide(
        PathList river,
        float startDist,
        float dir,
        float sampleLen,
        float step,
        float offset,
        List<float> heights,
        ref float widthSum,
        ref int widthN)
    {
        TerrainHeightMap heightMap = TerrainMeta.HeightMap;
        float len = river.Path.Length;
        float baseR = river.Width * 0.5f;

        for (float t = 0f; t <= sampleLen; t += step)
        {
            float d = startDist + dir * t;
            if (d < 0f || d > len)
                continue;

            Vector3 pt = BridgeTerrain.SamplePoint(river, d);
            float pathBed = pt.y + offset;
            float h = heightMap.GetHeight(pt);

            // Road fill sits well above the carved bed — skip those samples.
            if (h > pathBed + 1.75f)
                continue;

            // Prefer the actual carved heightmap; fall back to path bed if slightly noisy.
            float sample = h <= pathBed + 1.25f ? h : pathBed;
            // Keep samples in a tight band around the path profile.
            sample = Mathf.Clamp(sample, pathBed - 1.25f, pathBed + 0.75f);
            heights.Add(sample);

            widthSum += PathList.GetRadius(d, len, baseR, river.RandomScale, scaleWidthWithLength: true);
            widthN++;
        }
    }

    private static float RobustAverage(List<float> values)
    {
        if (values.Count == 0)
            return 0f;
        values.Sort();
        // Trim outer 15% each side when we have enough samples.
        int trim = values.Count >= 8 ? Mathf.Max(1, values.Count / 6) : 0;
        float sum = 0f;
        int n = 0;
        for (int i = trim; i < values.Count - trim; i++)
        {
            sum += values[i];
            n++;
        }
        return n > 0 ? sum / n : values[values.Count / 2];
    }

    private static int CarveThroughAnchors(
        BridgeCrossing crossing,
        PathList river,
        BedAnchors anchors)
    {
        TerrainHeightMap heightMap = TerrainMeta.HeightMap;
        var cfg = RoadFixConfig.Config;
        // Bank skirt past the channel — must reach past the ridge crests in screenshots.
        float softPad = Mathf.Clamp(cfg.LocalRiverOuterFade, 10f, 28f);

        // Full corridor: entire before-sample → after-sample stretch through the crossing.
        float corridor0 = Mathf.Max(0f, anchors.Corridor0 - softPad);
        float corridor1 = Mathf.Min(river.Path.Length, anchors.Corridor1 + softPad);
        float widthHalf = anchors.WidthHalf;
        // Small core + long rim = gentle side banks instead of sharp U walls.
        float coreFrac = widthHalf < 8f ? 0.12f : 0.2f;
        float rim = widthHalf + softPad;

        float searchR = rim + (corridor1 - corridor0) * 0.5f + 10f;
        Vector3 center = crossing.Center;
        center.y = 0f;

        // Track touched cells for a follow-up smooth pass.
        var touched = new HashSet<long>();
        int cells = 0;

        heightMap.ForEach(center, searchR, (x, z) =>
        {
            float nx = heightMap.Coordinate(x);
            float nz = heightMap.Coordinate(z);
            Vector3 world = TerrainMeta.Denormalize(new Vector3(nx, 0f, nz));

            if (!TryClosestOnPath(river, corridor0, corridor1, world, out float distRiver, out _, out float riverD))
                return;
            if (distRiver > rim)
                return;

            // Bed grade from before → after along the river.
            float tGrade = Mathf.InverseLerp(anchors.BeforeDist, anchors.AfterDist, riverD);
            tGrade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tGrade));
            float bedY = Mathf.Lerp(anchors.BeforeY, anchors.AfterY, tGrade);

            // Outer bank target eases up from bed toward current terrain so ridges
            // get shaved down without digging a second trench outside the river.
            float cur01 = heightMap.GetHeight01(x, z);
            float curY = TerrainMeta.DenormalizeY(cur01);
            float bankT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(widthHalf * coreFrac, rim, distRiver));
            float targetY = Mathf.Lerp(bedY, Mathf.Max(bedY, curY), bankT * 0.85f);
            float target01 = TerrainMeta.NormalizeY(targetY);

            // Lateral strength: full in channel, still meaningful past widthHalf to knock crests.
            float latT = InnerSlope(distRiver, widthHalf * coreFrac, rim);
            if (distRiver > widthHalf)
            {
                float skirt = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(widthHalf, rim, distRiver));
                latT = Mathf.Max(latT, skirt * 0.7f);
            }

            float endT = 1f;
            if (riverD < anchors.Corridor0)
                endT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(corridor0, anchors.Corridor0, riverD));
            else if (riverD > anchors.Corridor1)
                endT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(corridor1, anchors.Corridor1, riverD));

            float slope = latT * endT;
            if (slope <= 0.01f)
                return;

            float apply01 = Mathf.Lerp(cur01, target01, slope);
            if (cur01 <= apply01 + 0.0001f)
                return;

            heightMap.LowerHeight(x, z, apply01, 1f);
            touched.Add(Pack(x, z));
            cells++;
        });

        if (touched.Count > 0)
            SmoothTouched(heightMap, touched, passes: 4, expandRings: 3);

        return cells;
    }

    private static void SmoothTouched(
        TerrainHeightMap heightMap,
        HashSet<long> touched,
        int passes,
        int expandRings)
    {
        // Expand outward so bank ridges beyond the carve get pulled into the blend.
        var domain = new HashSet<long>(touched);
        for (int ring = 0; ring < expandRings; ring++)
        {
            var add = new List<long>();
            foreach (long key in domain)
            {
                Unpack(key, out int x, out int z);
                for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                    add.Add(Pack(x + ox, z + oz));
            }
            foreach (long k in add)
                domain.Add(k);
        }

        for (int pass = 0; pass < passes; pass++)
        {
            var next = new Dictionary<long, float>(domain.Count);
            foreach (long key in domain)
            {
                Unpack(key, out int x, out int z);
                float sum = 0f;
                int n = 0;
                // 5x5 kernel on carved cells for wider bank softening.
                int rad = touched.Contains(key) ? 2 : 1;
                for (int ox = -rad; ox <= rad; ox++)
                for (int oz = -rad; oz <= rad; oz++)
                {
                    sum += heightMap.GetHeight01(x + ox, z + oz);
                    n++;
                }

                float avg = sum / n;
                float cur = heightMap.GetHeight01(x, z);
                float strength = touched.Contains(key) ? 0.65f : 0.4f;
                next[key] = Mathf.Lerp(cur, avg, strength);
            }

            foreach (var kv in next)
            {
                Unpack(kv.Key, out int x, out int z);
                float nx = heightMap.Coordinate(x);
                float nz = heightMap.Coordinate(z);
                heightMap.SetHeight(nx, nz, kv.Value, 1f);
            }
        }
    }

    private static long Pack(int x, int z) => ((long)x << 32) ^ (uint)z;

    private static void Unpack(long key, out int x, out int z)
    {
        x = (int)(key >> 32);
        z = (int)(key & 0xFFFFFFFF);
    }

    private static float InnerSlope(float dist, float core, float rim)
    {
        if (dist <= core)
            return 1f;
        if (dist >= rim)
            return 0f;
        float t = Mathf.InverseLerp(core, rim, dist);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.SmoothStep(0f, 1f, t));
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

        float step = 1f;
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
