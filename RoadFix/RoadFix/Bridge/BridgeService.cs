using System.Collections.Generic;
using UnityEngine;

namespace RoadFix.Bridge;

/// <summary>
/// Find river crossings + place bridges. Never modifies road/rail path nodes.
/// Terrain fix is river height re-apply only (see GenerateRoadTerrain postfix).
/// </summary>
internal static class BridgeService
{
    private static readonly List<BridgeCrossing> RoadCrossings = new List<BridgeCrossing>();
    private static readonly List<BridgeCrossing> RailCrossings = new List<BridgeCrossing>();
    private static bool _roadBridgesPlaced;

    public static int RoadCrossingCount => RoadCrossings.Count;
    public static int RailCrossingCount => RailCrossings.Count;

    public static IReadOnlyList<BridgeCrossing> GetRoadCrossings() => RoadCrossings;
    public static IReadOnlyList<BridgeCrossing> GetRailCrossings() => RailCrossings;

    /// <summary>Detect road river spans (for bridges + skip AddToHeightMap). Path nodes untouched.</summary>
    public static void PrepareRoadCrossings()
    {
        var cfg = RoadFixConfig.Config;
        RoadCrossings.Clear();
        _roadBridgesPlaced = false;
        List<PathList> roads = TerrainPathAccess.GetRoads(TerrainMeta.Path);
        if (cfg == null || roads == null)
            return;

        RiverProximity.EnsureCache();
        List<BridgeCrossing> crossings = BridgeCrossingFinder.FindCrossings(roads, roadsOnlyHierarchy: true);
        RoadCrossings.AddRange(crossings);

        if (cfg.DebugLogging)
            Debug.Log($"[RoadFix] Found {crossings.Count} road bridge span(s) (paths untouched)");
    }

    /// <summary>Detect rail river spans. Path nodes untouched.</summary>
    public static void PrepareRailCrossings()
    {
        var cfg = RoadFixConfig.Config;
        RailCrossings.Clear();
        List<PathList> rails = TerrainPathAccess.GetRails(TerrainMeta.Path);
        if (cfg == null || !cfg.SpawnCustomBridges || rails == null)
            return;

        RiverProximity.EnsureCache();
        List<BridgeCrossing> crossings = BridgeCrossingFinder.FindCrossings(rails, roadsOnlyHierarchy: false);
        RailCrossings.AddRange(crossings);

        if (cfg.DebugLogging)
            Debug.Log($"[RoadFix] Found {crossings.Count} rail bridge span(s) (paths untouched)");
    }

    public static void PlaceRoadBridgesOnly()
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null || !cfg.SpawnCustomBridges || _roadBridgesPlaced)
            return;

        if (World.Cached)
        {
            if (cfg.DebugLogging)
                Debug.Log("[RoadFix] Skipping road bridge place on cached map (already spawned from .map)");
            _roadBridgesPlaced = true;
            return;
        }

        if (RoadCrossings.Count == 0)
            PrepareRoadCrossings();

        int placed = 0;
        foreach (BridgeCrossing crossing in RoadCrossings)
            placed += BridgeMapPlacer.PlaceCrossing(crossing, cfg.RoadBridgeMapPath, cfg.RoadPathCenterLocal);
        _roadBridgesPlaced = true;

        if (cfg.DebugLogging)
            Debug.Log($"[RoadFix] Road bridges queued rows: {placed}");
    }

    public static void PlaceRailBridges()
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null || !cfg.SpawnCustomBridges)
            return;

        if (World.Cached)
        {
            if (cfg.DebugLogging)
                Debug.Log("[RoadFix] Skipping rail bridge place on cached map (already spawned from .map)");
            return;
        }

        if (RailCrossings.Count == 0)
            PrepareRailCrossings();

        int placed = 0;
        foreach (BridgeCrossing crossing in RailCrossings)
            placed += BridgeMapPlacer.PlaceCrossing(crossing, cfg.RailBridgeMapPath, cfg.RailPathCenterLocal);

        if (cfg.DebugLogging)
            Debug.Log($"[RoadFix] Rail bridges queued rows: {placed}");
    }

    public static bool IsNearRoadCrossing(Vector3 worldPos, float pad = 8f)
    {
        foreach (BridgeCrossing c in RoadCrossings)
        {
            if (IsNearSpan(worldPos, c, pad))
                return true;
        }
        return false;
    }

    private static bool IsNearSpan(Vector3 worldPos, BridgeCrossing c, float pad)
    {
        if (c.Path?.Path == null)
            return false;
        float mid = (c.StartDist + c.EndDist) * 0.5f;
        Vector3 center = c.Path.Spline ? c.Path.Path.GetPointCubicHermite(mid) : c.Path.Path.GetPoint(mid);
        float dx = worldPos.x - center.x;
        float dz = worldPos.z - center.z;
        float rad = c.SpanLength * 0.5f + pad;
        return dx * dx + dz * dz <= rad * rad;
    }
}
