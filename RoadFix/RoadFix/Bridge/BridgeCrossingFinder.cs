using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadFix.Bridge;

internal static class BridgeCrossingFinder
{
    public static List<BridgeCrossing> FindCrossings(IList<PathList> paths, bool roadsOnlyHierarchy)
    {
        var cfg = RoadFixConfig.Config;
        var results = new List<BridgeCrossing>();
        List<PathList> rivers = TerrainPathAccess.GetRivers(TerrainMeta.Path);
        if (cfg == null || rivers == null || rivers.Count == 0)
            return results;

        float step = Mathf.Max(1f, cfg.CrossingSampleStep);
        float detectR = Mathf.Max(4f, cfg.CrossingDetectRadius);
        float minSpan = Mathf.Max(1f, cfg.MinBridgeSpanLength);
        float clearance = Mathf.Max(0f, cfg.WaterClearance);

        foreach (PathList path in paths)
        {
            if (path?.Path == null || path.Path.Length < minSpan)
                continue;
            if (roadsOnlyHierarchy && path.Hierarchy >= 2)
                continue;

            bool inSpan = false;
            float spanStart = 0f;
            float spanBedSum = 0f;
            int spanBedCount = 0;
            Vector3 spanCenterSum = Vector3.zero;
            Vector3 spanTangentSum = Vector3.zero;
            int spanPointCount = 0;

            for (float d = 0f; d <= path.Path.Length; d += step)
            {
                Vector3 pt = path.Spline ? path.Path.GetPointCubicHermite(d) : path.Path.GetPoint(d);
                Vector3 tan = path.Path.GetTangent(d);
                // Channel only (GetRadius) — riverside topology inflated spans into high banks.
                bool crossing = RiverProximity.IsInRiverChannel(pt, padMetres: 1.25f);
                float bedY = pt.y;
                if (crossing)
                {
                    RiverProximity.TryGetRiverBedY(pt, detectR, out bedY);
                }
                else
                {
                    // Water under a raised path, but still must sit inside the river channel radius.
                    float waterY = WaterLevel.RaycastWaterColliders(pt);
                    float terrainY = TerrainMeta.HeightMap.GetHeight(pt);
                    if (pt.y > waterY + clearance
                        && terrainY < waterY + 1f
                        && RiverProximity.IsInRiverChannel(pt, padMetres: 2.5f))
                    {
                        crossing = true;
                        bedY = Mathf.Min(terrainY, waterY);
                    }
                }

                if (crossing)
                {
                    if (!inSpan)
                    {
                        inSpan = true;
                        spanStart = d;
                        spanBedSum = 0f;
                        spanBedCount = 0;
                        spanCenterSum = Vector3.zero;
                        spanTangentSum = Vector3.zero;
                        spanPointCount = 0;
                    }
                    spanBedSum += bedY;
                    spanBedCount++;
                    spanCenterSum += pt;
                    spanTangentSum += new Vector3(tan.x, 0f, tan.z).normalized;
                    spanPointCount++;
                }
                else if (inSpan)
                {
                    TryAddSpan(results, path, spanStart, d - step, spanCenterSum, spanTangentSum, spanPointCount, spanBedSum, spanBedCount, minSpan);
                    inSpan = false;
                }
            }

            if (inSpan)
            {
                TryAddSpan(results, path, spanStart, path.Path.Length, spanCenterSum, spanTangentSum, spanPointCount, spanBedSum, spanBedCount, minSpan);
            }
        }

        // Rails often fork/merge just before water — keep one bridge per river gap.
        if (!roadsOnlyHierarchy)
            results = DeduplicateNearbySpans(results, cfg.RailBridgeMergeRadius);

        return results;
    }

    /// <summary>
    /// Junction / parallel rails: drop spans that sit on the same river crossing as a longer one.
    /// Uses center distance OR distance to the kept span's centerline (catches side-by-side decks).
    /// </summary>
    public static List<BridgeCrossing> DeduplicateNearbySpans(List<BridgeCrossing> spans, float mergeRadius)
    {
        float r = Mathf.Max(12f, mergeRadius);
        var ordered = spans.OrderByDescending(s => s.SpanLength).ToList();
        var kept = new List<BridgeCrossing>();

        foreach (BridgeCrossing candidate in ordered)
        {
            bool overlaps = false;
            foreach (BridgeCrossing existing in kept)
            {
                if (SpansShareCrossing(candidate, existing, r))
                {
                    overlaps = true;
                    break;
                }
            }
            if (!overlaps)
                kept.Add(candidate);
        }

        if (RoadFixConfig.Config?.DebugLogging == true && kept.Count != spans.Count)
        {
            Debug.Log(
                $"[RoadFix] Rail bridge dedupe: {spans.Count} → {kept.Count} " +
                $"(mergeRadius={r:F0}m, kept longest spans)");
        }

        return kept;
    }

    private static bool SpansShareCrossing(BridgeCrossing a, BridgeCrossing b, float radius)
    {
        float dx = a.Center.x - b.Center.x;
        float dz = a.Center.z - b.Center.z;
        if (dx * dx + dz * dz <= radius * radius)
            return true;

        // Side-by-side: candidate center near the other span's segment.
        if (DistanceToSpanSegmentSq(a.Center, b) <= radius * radius)
            return true;
        if (DistanceToSpanSegmentSq(b.Center, a) <= radius * radius)
            return true;

        // Parallel and close: similar tangent + lateral offset only.
        float dot = Mathf.Abs(Vector3.Dot(a.Tangent, b.Tangent));
        if (dot >= 0.85f)
        {
            Vector3 ab = a.Center - b.Center;
            ab.y = 0f;
            float along = Vector3.Dot(ab, b.Tangent);
            Vector3 lateral = ab - b.Tangent * along;
            if (lateral.sqrMagnitude <= radius * radius && Mathf.Abs(along) <= Mathf.Max(a.SpanLength, b.SpanLength) * 0.75f)
                return true;
        }

        return false;
    }

    private static float DistanceToSpanSegmentSq(Vector3 point, BridgeCrossing span)
    {
        if (span.Path?.Path == null)
            return float.MaxValue;

        Vector3 a = BridgeTerrain.SamplePoint(span.Path, span.StartDist);
        Vector3 b = BridgeTerrain.SamplePoint(span.Path, span.EndDist);
        a.y = 0f;
        b.y = 0f;
        point.y = 0f;

        Vector3 ab = b - a;
        float abLenSq = ab.sqrMagnitude;
        if (abLenSq < 0.0001f)
            return (point - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / abLenSq);
        Vector3 closest = a + ab * t;
        return (point - closest).sqrMagnitude;
    }

    private static void TryAddSpan(
        List<BridgeCrossing> results,
        PathList path,
        float start,
        float end,
        Vector3 centerSum,
        Vector3 tangentSum,
        int pointCount,
        float bedSum,
        int bedCount,
        float minSpan)
    {
        float len = end - start;
        if (len < minSpan || pointCount <= 0)
            return;

        Vector3 tangent = tangentSum.sqrMagnitude > 0.0001f
            ? tangentSum.normalized
            : Flatten(path.Path.GetTangent((start + end) * 0.5f));
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.forward;

        float nodeLen = Mathf.Max(1f, RoadFixConfig.Config?.BridgeTemplateLength ?? 12f);
        int nodes = Mathf.Max(1, Mathf.RoundToInt(len / nodeLen));
        Vector3 center = centerSum / pointCount;
        var (startY, endY) = BridgeTerrain.ComputeBankHeights(path, start, end);
        float deckY = (startY + endY) * 0.5f;

        results.Add(new BridgeCrossing
        {
            Path = path,
            StartDist = start,
            EndDist = end,
            Center = new Vector3(center.x, deckY, center.z),
            Tangent = tangent,
            SpanLength = len,
            RiverBedY = bedCount > 0 ? bedSum / bedCount : center.y - 4f,
            NodeCount = nodes,
            DeckY = deckY,
            StartDeckY = startY,
            EndDeckY = endY
        });
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}
