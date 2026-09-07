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
        float maxLat = Mathf.Clamp(RoadFixConfig.Config?.RailDeckLateralMerge ?? 16f, 8f, 28f);
        var ordered = spans.OrderByDescending(s => s.SpanLength).ToList();
        var kept = new List<BridgeCrossing>();

        foreach (BridgeCrossing candidate in ordered)
        {
            bool overlaps = false;
            foreach (BridgeCrossing existing in kept)
            {
                if (SpansShareCrossing(candidate, existing, r, maxLat))
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
                $"(mergeRadius={r:F0}m lateral={maxLat:F0}m, kept longest spans)");
        }

        if (RoadFixConfig.Config?.WidenRailBridgeForParallelTracks == true)
        {
            ApplyRailDeckCover(kept, ordered);
            ClampOverlappingRailDecks(kept);
        }

        return kept;
    }

    /// <summary>
    /// Road + rail on the same river gap: keep one widened road tile, skip the rail tile.
    /// Parallel extra rails stay snapped to that same deck.
    /// </summary>
    public static void MergeTouchingRoadRail(List<BridgeCrossing> roads, List<BridgeCrossing> rails)
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null)
            return;

        if (cfg.MergeTouchingRoadRail && roads != null && rails != null
            && roads.Count > 0 && rails.Count > 0)
            MergeTouchingRoadRailCore(roads, rails, cfg);

        ApplyRoadDeckCover(roads);
    }

    private static void MergeTouchingRoadRailCore(
        List<BridgeCrossing> roads,
        List<BridgeCrossing> rails,
        RoadFixConfig.ConfigData cfg)
    {
        float touch = Mathf.Clamp(cfg.RoadRailTouchDistance, 8f, 28f);
        float pad = Mathf.Max(0f, cfg.RailBridgeWidthPad);

        for (int i = 0; i < roads.Count; i++)
        {
            BridgeCrossing road = roads[i];
            road.SkipPlace = false;
            road.CoverWidth = 0f;
            roads[i] = road;
        }
        for (int i = 0; i < rails.Count; i++)
        {
            BridgeCrossing rail = rails[i];
            rail.SkipPlace = false;
            rail.UseRoadDeck = false;
            MarkExtraRoadDeck(rail.ExtraSpans, false);
            rails[i] = rail;
        }

        for (int r = 0; r < roads.Count; r++)
        {
            BridgeCrossing road = roads[r];
            if (road.Path?.Path == null)
                continue;

            Vector3 p0 = BridgeTerrain.SamplePoint(road.Path, road.StartDist);
            Vector3 p1 = BridgeTerrain.SamplePoint(road.Path, road.EndDist);
            p0.y = 0f;
            p1.y = 0f;
            Vector3 mid = (p0 + p1) * 0.5f;
            Vector3 tan = Flatten(road.Tangent);
            if (tan.sqrMagnitude < 0.0001f)
                tan = Flatten(p1 - p0);
            if (tan.sqrMagnitude < 0.0001f)
                tan = Vector3.forward;
            tan.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, tan);
            side.y = 0f;
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.right;
            side.Normalize();

            float roadHalf = PathHalfWidth(road.Path, 5f);
            float minL = -roadHalf;
            float maxL = roadHalf;
            int absorbed = 0;
            float maxSpan = road.SpanLength;
            int maxNodes = road.NodeCount;

            for (int k = 0; k < rails.Count; k++)
            {
                BridgeCrossing rail = rails[k];
                if (rail.SkipPlace || rail.Path?.Path == null)
                    continue;
                if (!SpansShareCrossing(road, rail, touch, touch))
                    continue;

                float railHalf = PathHalfWidth(rail.Path, 2f);
                AccumulateLateral(rail.Path, p0, p1, mid, side, railHalf, touch + railHalf, ref minL, ref maxL);
                if (rail.ExtraSpans != null)
                {
                    for (int e = 0; e < rail.ExtraSpans.Count; e++)
                    {
                        PathList extraPath = rail.ExtraSpans[e].Path;
                        if (extraPath?.Path == null)
                            continue;
                        AccumulateLateral(extraPath, p0, p1, mid, side, PathHalfWidth(extraPath, 2f),
                            touch + 4f, ref minL, ref maxL);
                    }
                }

                rail.SkipPlace = true;
                rail.UseRoadDeck = true;
                MarkExtraRoadDeck(rail.ExtraSpans, true);
                rails[k] = rail;
                absorbed++;
                if (rail.SpanLength > maxSpan)
                    maxSpan = rail.SpanLength;
                if (rail.NodeCount > maxNodes)
                    maxNodes = rail.NodeCount;
            }

            if (absorbed == 0)
                continue;

            float mul = Mathf.Max(1f, cfg.CombinedBridgeWidthMultiplier);
            road.CoverWidth = ((maxL - minL) + pad) * mul;
            road.SpanLength = maxSpan;
            road.NodeCount = maxNodes;
            road.Center = new Vector3(
                mid.x + side.x * ((minL + maxL) * 0.5f),
                road.Center.y,
                mid.z + side.z * ((minL + maxL) * 0.5f));
            roads[r] = road;

            if (cfg.DebugLogging)
            {
                Debug.Log(
                    $"[RoadFix] Combined road+rail '{road.Path.Name}' absorbedRails={absorbed} " +
                    $"lateral={minL:F1}..{maxL:F1} coverWidth={road.CoverWidth:F1}m " +
                    $"widthMul={mul:F2} span={road.SpanLength:F1} nativeClear={cfg.RoadBridgeNativeWidth:F1}");
            }
        }
    }

    /// <summary>
    /// Solo road tiles otherwise stay at native U-channel width, which is narrower
    /// than the 10–12 m asphalt. Floor cover so walls sit outside the road mesh.
    /// Combined road+rail covers (already larger) are kept.
    /// </summary>
    private static void ApplyRoadDeckCover(List<BridgeCrossing> roads)
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null || roads == null)
            return;

        float pad = Mathf.Max(0f, cfg.RoadBridgeWidthPad);
        for (int i = 0; i < roads.Count; i++)
        {
            BridgeCrossing road = roads[i];
            float roadW = PathHalfWidth(road.Path, 5f) * 2f;
            float need = roadW + pad * 2f;
            if (need <= 1f)
                continue;
            if (need > road.CoverWidth)
                road.CoverWidth = need;
            roads[i] = road;
            if (cfg.DebugLogging)
            {
                Debug.Log(
                    $"[RoadFix] Road deck cover '{road.Path?.Name}' roadW={roadW:F1} " +
                    $"+{pad:F1}m/edge cover={road.CoverWidth:F1}m nativeClear={cfg.RoadBridgeNativeWidth:F1}");
            }
        }
    }

    private static void MarkExtraRoadDeck(List<BridgeCrossing> extras, bool useRoadDeck)
    {
        if (extras == null)
            return;
        for (int i = 0; i < extras.Count; i++)
        {
            BridgeCrossing extra = extras[i];
            extra.UseRoadDeck = useRoadDeck;
            extras[i] = extra;
        }
    }

    private static float PathHalfWidth(PathList path, float fallback)
    {
        return path != null && path.Width > 0.5f ? path.Width * 0.5f : fallback;
    }

    private static void AccumulateLateral(
        PathList path,
        Vector3 spanA,
        Vector3 spanB,
        Vector3 mid,
        Vector3 side,
        float halfWidth,
        float maxDist,
        ref float minL,
        ref float maxL)
    {
        if (!TryClosestOnRailToSpan(path, spanA, spanB, maxDist, out Vector3 hit, out _))
            return;
        float lat = side.x * (hit.x - mid.x) + side.z * (hit.z - mid.z);
        if (lat - halfWidth < minL) minL = lat - halfWidth;
        if (lat + halfWidth > maxL) maxL = lat + halfWidth;
    }

    /// <summary>
    /// Widen only for rails that join this host (switch / shared train path).
    /// A parallel crossing with its own tile is not an extra — that was widening
    /// the wrong deck into its neighbour. Each branch is assigned to the nearest host.
    /// </summary>
    private static void ApplyRailDeckCover(List<BridgeCrossing> kept, List<BridgeCrossing> allSpans)
    {
        var cfg = RoadFixConfig.Config;
        _ = allSpans;
        List<PathList> rails = TerrainPathAccess.GetRails(TerrainMeta.Path);
        if (cfg == null || rails == null || rails.Count == 0 || kept == null)
            return;

        float joinM = Mathf.Clamp(cfg.RailJoinDistance, 2.5f, 8f);
        float pad = Mathf.Max(0f, cfg.RailBridgeWidthPad);
        float spread = Mathf.Clamp(cfg.RailJunctionDetectRadius, 8f, 28f);
        var claims = new List<RailExtraClaim>();

        for (int k = 0; k < kept.Count; k++)
        {
            BridgeCrossing host = kept[k];
            if (host.Path?.Path == null)
                continue;

            for (int r = 0; r < rails.Count; r++)
            {
                PathList rail = rails[r];
                if (rail?.Path == null || ReferenceEquals(rail, host.Path))
                    continue;
                if (KeptContainsPath(kept, rail, k))
                    continue;
                if (!RailsJoinNearCrossing(host.Path, host.StartDist, host.EndDist, rail, joinM))
                    continue;

                float dist = MinDistToRailAlongSpan(host.Path, host.StartDist, host.EndDist, rail);
                claims.Add(new RailExtraClaim
                {
                    Rail = rail,
                    HostIndex = k,
                    Dist = dist
                });
            }
        }

        // One extra → one host (the closest join). Stops two tiles both widening
        // toward a rail that sits between them.
        var winner = new Dictionary<PathList, RailExtraClaim>();
        for (int i = 0; i < claims.Count; i++)
        {
            RailExtraClaim c = claims[i];
            if (!winner.TryGetValue(c.Rail, out RailExtraClaim prev) || c.Dist < prev.Dist)
                winner[c.Rail] = c;
        }

        var extrasByHost = new List<PathList>[kept.Count];
        foreach (RailExtraClaim c in winner.Values)
        {
            extrasByHost[c.HostIndex] ??= new List<PathList>();
            extrasByHost[c.HostIndex].Add(c.Rail);
            if (cfg.DebugLogging)
            {
                Debug.Log(
                    $"[RoadFix] Rail join '{c.Rail.Name}' → '{kept[c.HostIndex].Path?.Name}' " +
                    $"dist={c.Dist:F1}m (not a parallel neighbour)");
            }
        }

        float mul = Mathf.Max(1f, cfg.DualRailCoverMultiplier);
        for (int k = 0; k < kept.Count; k++)
        {
            List<PathList> extras = extrasByHost[k];
            if (extras == null || extras.Count == 0)
                continue;

            BridgeCrossing host = kept[k];
            Vector3 p0 = BridgeTerrain.SamplePoint(host.Path, host.StartDist);
            Vector3 p1 = BridgeTerrain.SamplePoint(host.Path, host.EndDist);
            p0.y = 0f;
            p1.y = 0f;
            Vector3 mid = (p0 + p1) * 0.5f;
            Vector3 tan = host.Tangent;
            tan.y = 0f;
            if (tan.sqrMagnitude < 0.0001f)
                tan = p1 - p0;
            tan.y = 0f;
            if (tan.sqrMagnitude < 0.0001f)
                tan = Vector3.forward;
            tan.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, tan);
            side.y = 0f;
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.right;
            side.Normalize();

            float hostHalf = PathHalfWidth(host.Path, 2f);
            float minL = -hostHalf;
            float maxL = hostHalf;
            float extend = 12f;
            Vector3 p0x = p0 - tan * extend;
            Vector3 p1x = p1 + tan * extend;
            List<BridgeCrossing> extraSpans = new List<BridgeCrossing>();

            for (int e = 0; e < extras.Count; e++)
            {
                PathList rail = extras[e];
                if (!TryLateralAlongSpan(rail, p0x, p1x, mid, side, spread, PathHalfWidth(rail, 2f),
                        ref minL, ref maxL, out float d0, out float d1))
                    continue;
                extraSpans.Add(MakeAlongsideSpan(rail, host, d0, d1));
            }

            if (extraSpans.Count == 0)
                continue;

            host.ExtraSpans = extraSpans;
            host.CoverWidth = ((maxL - minL) + pad * 2f) * mul;
            host.Center = new Vector3(
                mid.x + side.x * ((minL + maxL) * 0.5f),
                host.Center.y,
                mid.z + side.z * ((minL + maxL) * 0.5f));
            kept[k] = host;

            if (cfg.DebugLogging)
            {
                Debug.Log(
                    $"[RoadFix] Rail deck cover '{host.Path.Name}' joinedRails={extraSpans.Count} " +
                    $"lateral={minL:F1}..{maxL:F1} coverWidth={host.CoverWidth:F1}m " +
                    $"mul={mul:F2} nativeClear={cfg.RailBridgeNativeWidth:F1}");
            }
        }
    }

    private struct RailExtraClaim
    {
        public PathList Rail;
        public int HostIndex;
        public float Dist;
    }

    /// <summary>
    /// True when two rail paths share a train connection: endpoints meet, or they
    /// merge within joinM on this river span (switch on the deck).
    /// </summary>
    private static bool RailsJoinNearCrossing(
        PathList host,
        float start,
        float end,
        PathList other,
        float joinM)
    {
        if (host?.Path == null || other?.Path == null)
            return false;

        if (TryClosestOnRailToPoint(other, host.Path.GetStartPoint(), joinM, out _, out _))
            return true;
        if (TryClosestOnRailToPoint(other, host.Path.GetEndPoint(), joinM, out _, out _))
            return true;
        if (TryClosestOnRailToPoint(host, other.Path.GetStartPoint(), joinM, out _, out _))
            return true;
        if (TryClosestOnRailToPoint(host, other.Path.GetEndPoint(), joinM, out _, out _))
            return true;

        float pathLen = host.Path.Length;
        float s = Mathf.Max(0f, start - 12f);
        float e = Mathf.Min(pathLen, end + 12f);
        for (int i = 0; i <= 8; i++)
        {
            float d = Mathf.Lerp(s, e, i / 8f);
            Vector3 pt = BridgeTerrain.SamplePoint(host, d);
            if (TryClosestOnRailToPoint(other, pt, joinM, out _, out _))
                return true;
        }
        return false;
    }

    private static float MinDistToRailAlongSpan(PathList host, float start, float end, PathList other)
    {
        float best = float.MaxValue;
        float pathLen = host.Path.Length;
        float s = Mathf.Max(0f, start - 12f);
        float e = Mathf.Min(pathLen, end + 12f);
        for (int i = 0; i <= 8; i++)
        {
            float d = Mathf.Lerp(s, e, i / 8f);
            Vector3 pt = BridgeTerrain.SamplePoint(host, d);
            if (!TryClosestOnRailToPoint(other, pt, 80f, out Vector3 hit, out _))
                continue;
            float dx = hit.x - pt.x;
            float dz = hit.z - pt.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < best)
                best = dist;
        }
        return best;
    }

    /// <summary>
    /// After widening, shrink decks that would occupy the same river gap so they
    /// abut instead of overlapping. Native-width neighbours 20 m+ apart are left alone.
    /// </summary>
    private static void ClampOverlappingRailDecks(List<BridgeCrossing> kept)
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null || kept == null || kept.Count < 2)
            return;

        float native = Mathf.Max(4f, cfg.RailBridgeNativeWidth);
        const float abut = 0.5f;
        for (int pass = 0; pass < 6; pass++)
        {
            bool changed = false;
            for (int i = 0; i < kept.Count; i++)
            {
                BridgeCrossing a = kept[i];
                if (a.SkipPlace || a.Path?.Path == null)
                    continue;
                for (int j = i + 1; j < kept.Count; j++)
                {
                    BridgeCrossing b = kept[j];
                    if (b.SkipPlace || b.Path?.Path == null)
                        continue;

                    float sep = LateralOffset(a, b);
                    if (sep < 3f || sep > 80f)
                        continue;

                    float wa = a.CoverWidth > 1f ? a.CoverWidth : native;
                    float wb = b.CoverWidth > 1f ? b.CoverWidth : native;
                    float need = (wa + wb) * 0.5f + abut;
                    if (sep + 0.05f >= need)
                        continue;

                    float overflow = need - sep;
                    bool aWide = a.CoverWidth >= b.CoverWidth;
                    if (aWide && a.CoverWidth > native + 0.25f)
                    {
                        a.CoverWidth = Mathf.Max(native, a.CoverWidth - overflow * 2f);
                        kept[i] = a;
                        changed = true;
                        if (cfg.DebugLogging)
                        {
                            Debug.Log(
                                $"[RoadFix] Abut clamp '{a.Path.Name}' vs '{b.Path.Name}' " +
                                $"sep={sep:F1}m → cover={a.CoverWidth:F1}m");
                        }
                    }
                    else if (b.CoverWidth > native + 0.25f)
                    {
                        b.CoverWidth = Mathf.Max(native, b.CoverWidth - overflow * 2f);
                        kept[j] = b;
                        changed = true;
                        if (cfg.DebugLogging)
                        {
                            Debug.Log(
                                $"[RoadFix] Abut clamp '{b.Path.Name}' vs '{a.Path.Name}' " +
                                $"sep={sep:F1}m → cover={b.CoverWidth:F1}m");
                        }
                    }
                }
            }
            if (!changed)
                break;
        }
    }

    private static BridgeCrossing MakeAlongsideSpan(PathList rail, BridgeCrossing host, float d0, float d1)
    {
        if (d1 < d0)
        {
            float tmp = d0;
            d0 = d1;
            d1 = tmp;
        }
        return new BridgeCrossing
        {
            Path = rail,
            StartDist = d0,
            EndDist = d1,
            Center = host.Center,
            Tangent = host.Tangent,
            SpanLength = Mathf.Max(1f, d1 - d0),
            RiverBedY = host.RiverBedY,
            NodeCount = host.NodeCount,
            DeckY = host.DeckY,
            StartDeckY = host.StartDeckY,
            EndDeckY = host.EndDeckY,
            UseRoadDeck = host.UseRoadDeck
        };
    }

    /// <summary>
    /// Sample the extra rail beside several points on the host span so a Y-split at
    /// the abutment does not collapse lateral width to ~0.
    /// </summary>
    private static bool TryLateralAlongSpan(
        PathList rail,
        Vector3 spanA,
        Vector3 spanB,
        Vector3 mid,
        Vector3 side,
        float maxDist,
        float halfWidth,
        ref float minL,
        ref float maxL,
        out float railD0,
        out float railD1)
    {
        railD0 = float.MaxValue;
        railD1 = float.MinValue;
        bool hitAny = false;
        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f;
            Vector3 hostPt = Vector3.Lerp(spanA, spanB, t);
            if (!TryClosestOnRailToPoint(rail, hostPt, maxDist, out Vector3 hit, out float railD))
                continue;
            hitAny = true;
            float lat = side.x * (hit.x - mid.x) + side.z * (hit.z - mid.z);
            if (lat - halfWidth < minL) minL = lat - halfWidth;
            if (lat + halfWidth > maxL) maxL = lat + halfWidth;
            if (railD < railD0) railD0 = railD;
            if (railD > railD1) railD1 = railD;
        }
        if (!hitAny)
            return false;
        if (railD1 - railD0 < 1f)
        {
            float half = Vector3.Distance(spanA, spanB) * 0.5f;
            railD0 = Mathf.Max(0f, railD0 - half);
            railD1 = railD0 + Mathf.Max(2f, Vector3.Distance(spanA, spanB));
        }
        return true;
    }

    private static bool KeptContainsPath(List<BridgeCrossing> kept, PathList rail, int hostIndex)
    {
        for (int i = 0; i < kept.Count; i++)
        {
            if (i == hostIndex)
                continue;
            if (ReferenceEquals(kept[i].Path, rail))
                return true;
        }
        return false;
    }

    private static bool TryClosestOnRailToPoint(
        PathList rail,
        Vector3 worldPt,
        float maxDist,
        out Vector3 hit,
        out float railDist)
    {
        hit = default;
        railDist = 0f;
        float len = rail.Path.Length;
        if (len < 2f)
            return false;

        worldPt.y = 0f;
        float bestSq = maxDist * maxDist;
        bool found = false;
        float step = Mathf.Clamp(len / 80f, 2f, 8f);
        for (float d = 0f; d <= len; d += step)
        {
            Vector3 pt = BridgeTerrain.SamplePoint(rail, d);
            pt.y = 0f;
            float dx = pt.x - worldPt.x;
            float dz = pt.z - worldPt.z;
            float distSq = dx * dx + dz * dz;
            if (distSq <= bestSq)
            {
                bestSq = distSq;
                hit = pt;
                railDist = d;
                found = true;
            }
        }
        return found;
    }

    private static bool TryClosestOnRailToSpan(
        PathList rail,
        Vector3 spanA,
        Vector3 spanB,
        float maxDist,
        out Vector3 hit,
        out float dist)
    {
        hit = default;
        dist = float.MaxValue;
        float len = rail.Path.Length;
        if (len < 2f)
            return false;

        float step = Mathf.Clamp(len / 80f, 2f, 8f);
        Vector3 ab = spanB - spanA;
        float abLenSq = ab.sqrMagnitude;
        for (float d = 0f; d <= len; d += step)
        {
            Vector3 pt = BridgeTerrain.SamplePoint(rail, d);
            pt.y = 0f;
            Vector3 closest = spanA;
            if (abLenSq > 0.0001f)
            {
                float t = Mathf.Clamp01(Vector3.Dot(pt - spanA, ab) / abLenSq);
                closest = spanA + ab * t;
            }
            float dx = pt.x - closest.x;
            float dz = pt.z - closest.z;
            float distSq = dx * dx + dz * dz;
            if (distSq < dist * dist)
            {
                dist = Mathf.Sqrt(distSq);
                hit = pt;
            }
        }

        return dist <= maxDist;
    }

    private static bool SpansShareCrossing(BridgeCrossing a, BridgeCrossing b, float radius, float maxLateral)
    {
        float lat = LateralOffset(a, b);
        if (lat > maxLateral)
            return false;

        Vector3 ab = a.Center - b.Center;
        ab.y = 0f;
        Vector3 tan = Flatten(b.Tangent);
        float along = Mathf.Abs(Vector3.Dot(ab, tan));
        float spanMax = Mathf.Max(a.SpanLength, b.SpanLength);
        // Same corridor but different river gaps (further along the path).
        if (along > Mathf.Max(24f, spanMax * 0.8f))
            return false;

        float dx = a.Center.x - b.Center.x;
        float dz = a.Center.z - b.Center.z;
        if (dx * dx + dz * dz <= radius * radius)
            return true;

        if (DistanceToSpanSegmentSq(a.Center, b) <= radius * radius)
            return true;
        if (DistanceToSpanSegmentSq(b.Center, a) <= radius * radius)
            return true;

        return false;
    }

    private static float LateralOffset(BridgeCrossing a, BridgeCrossing b)
    {
        Vector3 tan = Flatten(a.Tangent);
        Vector3 side = Vector3.Cross(Vector3.up, tan);
        side.y = 0f;
        if (side.sqrMagnitude < 0.0001f)
            side = Vector3.right;
        side.Normalize();
        Vector3 ab = b.Center - a.Center;
        ab.y = 0f;
        return Mathf.Abs(side.x * ab.x + side.z * ab.z);
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

        if (!IsTrueRiverCrossing(path, start, end, tangent, len, out string skipReason))
        {
            if (RoadFixConfig.Config?.DebugLogging == true)
            {
                Debug.Log(
                    $"[RoadFix] Skip non-crossing '{path.Name}' span={start:F0}-{end:F0} " +
                    $"len={len:F1}m ({skipReason})");
            }
            return;
        }

        float channelLen = end - start;
        RiverProximity.ExpandSpanToWater(path, ref start, ref end);
        // The midpoint radius underestimates some curved/widening rivers, which can
        // leave deck edges short on one side. Probe a few along-span points and use
        // the max radius to enforce enough minimum water length.
        float spanLenAfter = end - start;
        float radiusMax = 0f;
        float mid = (start + end) * 0.5f;
        float q = spanLenAfter * 0.25f;
        float[] probes = new float[]
        {
            mid,
            Mathf.Clamp(mid - q, 0f, path.Path.Length),
            Mathf.Clamp(mid + q, 0f, path.Path.Length)
        };

        for (int i = 0; i < probes.Length; i++)
        {
            Vector3 probePt = BridgeTerrain.SamplePoint(path, probes[i]);
            if (RiverProximity.TryGetChannelInfo(probePt, 12f, out _, out _, out float r))
            {
                if (r > radiusMax)
                    radiusMax = r;
            }
        }

        if (radiusMax > 0.01f)
        {
            float minWater = radiusMax * 2f;
            if (end - start < minWater)
            {
                float extra = (minWater - (end - start)) * 0.5f;
                start = Mathf.Max(0f, start - extra);
                end = Mathf.Min(path.Path.Length, end + extra);
            }
        }
        len = end - start;
        if (RoadFixConfig.Config?.DebugLogging == true && len > channelLen + 1.5f)
        {
            Debug.Log(
                $"[RoadFix] Water span '{path.Name}' channel={channelLen:F1}m → water={len:F1}m " +
                $"(+{len - channelLen:F1}m topology)");
        }

        float nodeLen = Mathf.Max(1f, RoadFixConfig.Config?.BridgeTemplateLength ?? 12f);
        int nodes = Mathf.Max(1, Mathf.RoundToInt(len / nodeLen));
        Vector3 midPt = BridgeTerrain.SamplePoint(path, (start + end) * 0.5f);
        var (startY, endY) = BridgeTerrain.ComputeBankHeights(path, start, end);
        float deckY = (startY + endY) * 0.5f;

        results.Add(new BridgeCrossing
        {
            Path = path,
            StartDist = start,
            EndDist = end,
            Center = new Vector3(midPt.x, deckY, midPt.z),
            Tangent = tangent,
            SpanLength = len,
            RiverBedY = bedCount > 0 ? bedSum / bedCount : midPt.y - 4f,
            NodeCount = nodes,
            DeckY = deckY,
            StartDeckY = startY,
            EndDeckY = endY
        });
    }

    /// <summary>
    /// True only when the path actually goes across the wet channel.
    /// Roads that follow a riverbank (or shoreline) must not get a bridge tile.
    /// </summary>
    private static bool IsTrueRiverCrossing(
        PathList path,
        float start,
        float end,
        Vector3 pathTangent,
        float spanLen,
        out string skipReason)
    {
        skipReason = null;
        Vector3 mid = BridgeTerrain.SamplePoint(path, (start + end) * 0.5f);
        if (!RiverProximity.TryGetChannelInfo(mid, 12f, out PathList river, out float riverDist, out float radius)
            || river?.Path == null)
        {
            skipReason = "no-river";
            return false;
        }

        Vector3 riverPt = river.Spline
            ? river.Path.GetPointCubicHermite(riverDist)
            : river.Path.GetPoint(riverDist);
        Vector3 riverTan = Flatten(river.Path.GetTangent(riverDist));
        Vector3 riverSide = Vector3.Cross(Vector3.up, riverTan);
        riverSide.y = 0f;
        if (riverSide.sqrMagnitude < 0.0001f)
            riverSide = Vector3.right;
        riverSide.Normalize();

        float pad = 12f;
        Vector3 before = BridgeTerrain.SamplePoint(path, Mathf.Max(0f, start - pad));
        Vector3 after = BridgeTerrain.SamplePoint(path, Mathf.Min(path.Path.Length, end + pad));
        float side0 = riverSide.x * (before.x - riverPt.x) + riverSide.z * (before.z - riverPt.z);
        float side1 = riverSide.x * (after.x - riverPt.x) + riverSide.z * (after.z - riverPt.z);
        bool crossed = side0 * side1 < 0f;
        float across = Mathf.Abs(side1 - side0);
        float channel = Mathf.Max(8f, radius * 2f);

        float alignment = Mathf.Abs(Vector3.Dot(Flatten(pathTangent), riverTan));
        // Shoreline follower: long, parallel, never leaves this bank.
        if (!crossed && alignment > 0.72f && spanLen > Mathf.Max(40f, channel * 1.25f))
        {
            skipReason = $"along-river dot={alignment:F2} side={side0:F1}→{side1:F1}";
            return false;
        }

        // Long graze of one bank. Short in-channel clips still get a bridge
        // (river spline is often off-centre in the water).
        if (!crossed && across < channel * 0.7f && spanLen > channel * 0.9f && alignment > 0.5f)
        {
            skipReason = $"same-bank side={side0:F1}→{side1:F1} across={across:F1} channel={channel:F1}";
            return false;
        }

        return true;
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}
