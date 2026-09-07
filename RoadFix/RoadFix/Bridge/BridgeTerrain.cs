using UnityEngine;

namespace RoadFix.Bridge;

internal static class BridgeTerrain
{
    public static void RecarveUnderSpan(BridgeCrossing crossing, bool force = false)
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null || crossing.Path?.Path == null)
            return;

        TerrainHeightMap heightMap = TerrainMeta.HeightMap;
        float halfWidth = Mathf.Max(4f, cfg.RecarveWidth * 0.5f);
        float step = 1.5f;
        float targetY = crossing.RiverBedY;
        int cells = 0;

        for (float d = crossing.StartDist; d <= crossing.EndDist; d += step)
        {
            Vector3 pt = SamplePoint(crossing.Path, d);
            Vector3 tan = crossing.Path.Path.GetTangent(d);
            tan.y = 0f;
            if (tan.sqrMagnitude < 0.0001f)
                continue;
            tan.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, tan).normalized;

            for (float s = -halfWidth; s <= halfWidth; s += 1.5f)
            {
                Vector3 sample = pt + side * s;
                float normX = TerrainMeta.NormalizeX(sample.x);
                float normZ = TerrainMeta.NormalizeZ(sample.z);
                float edge = 1f - Mathf.Clamp01(Mathf.Abs(s) / halfWidth);
                float opacity = force ? 1f : Mathf.SmoothStep(0.35f, 1f, edge);
                if (opacity <= 0f)
                    continue;

                float yn = TerrainMeta.NormalizeY(targetY);
                float current = heightMap.GetHeight01(normX, normZ);
                if (force || current > yn)
                {
                    heightMap.SetHeight(normX, normZ, yn, opacity);
                    cells++;
                }
            }
        }

        if (cfg.DebugLogging)
            Debug.Log($"[RoadFix] Recarved under span len={crossing.SpanLength:F1} bedY={targetY:F1} samples≈{cells} force={force}");
    }

    /// <summary>
    /// Flatten rail path nodes across a bridge span onto a straight grade between the
    /// approach nodes (aligns rail mesh with the shared bridge.map deck).
    /// </summary>
    public static void SnapRailNodesToDeckGrade(BridgeCrossing crossing)
    {
        if (crossing.Path?.Path?.Points == null)
            return;

        Vector3[] points = crossing.Path.Path.Points;
        float pathLen = crossing.Path.Path.Length;
        if (pathLen <= 0f || points.Length < 2)
            return;

        BridgeMapPlacer.GetRailDeckGrade(crossing, out float y0, out float y1);
        SnapRailNodesToDeckGrade(crossing, y0, y1);
    }

    public static void SnapRailNodesToDeckGrade(BridgeCrossing crossing, float y0, float y1)
    {
        if (crossing.Path?.Path?.Points == null)
            return;

        Vector3[] points = crossing.Path.Path.Points;
        float pathLen = crossing.Path.Path.Length;
        if (pathLen <= 0f || points.Length < 2)
            return;

        float fade = 10f;
        float d0 = Mathf.Max(0f, crossing.StartDist - fade);
        float d1 = Mathf.Min(pathLen, crossing.EndDist + fade);

        // Accumulate distance along polyline for accurate node→span mapping.
        float[] distAt = new float[points.Length];
        distAt[0] = 0f;
        for (int i = 1; i < points.Length; i++)
            distAt[i] = distAt[i - 1] + Vector3.Distance(points[i - 1], points[i]);

        int changed = 0;
        for (int i = 0; i < points.Length; i++)
        {
            float d = distAt[i];
            if (d < d0 || d > d1)
                continue;

            Vector3 p = points[i];
            float deckY;
            if (d < crossing.StartDist)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(d0, crossing.StartDist, d));
                deckY = Mathf.Lerp(p.y, y0, t);
            }
            else if (d > crossing.EndDist)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(d1, crossing.EndDist, d));
                deckY = Mathf.Lerp(p.y, y1, t);
            }
            else
            {
                float t = Mathf.InverseLerp(crossing.StartDist, crossing.EndDist, d);
                deckY = Mathf.Lerp(y0, y1, t);
            }

            if (Mathf.Abs(p.y - deckY) > 0.01f)
                changed++;
            p.y = deckY;
            points[i] = p;
        }

        crossing.Path.Path.RecalculateTangents();

        if (RoadFixConfig.Config?.DebugLogging == true)
        {
            Debug.Log(
                $"[RoadFix] SnapRail→deck '{crossing.Path.Name}' Y {y0:F2}→{y1:F2} " +
                $"span={crossing.StartDist:F0}-{crossing.EndDist:F0} " +
                $"changed={changed}/{points.Length}");
        }
    }

    /// <summary>
    /// Rail-style: path nodes follow the bank-to-bank slope (not terrain / not flat max).
    /// </summary>
    public static void ElevatePathAcrossSpan(BridgeCrossing crossing)
    {
        if (crossing.Path?.Path?.Points == null)
            return;

        Vector3[] points = crossing.Path.Path.Points;
        float pathLen = crossing.Path.Path.Length;
        if (pathLen <= 0f || points.Length < 2)
            return;

        float nodeLen = Mathf.Max(4f, RoadFixConfig.Config?.BridgeTemplateLength ?? 12f);
        SampleCenterNeighborNodes(
            crossing.Path, crossing.StartDist, crossing.EndDist, nodeLen,
            out _, out _, out float y0, out float y1, out _);

        // Fall back to bank heights / stored deck when neighbor nodes are flat.
        if (Mathf.Abs(y1 - y0) < 0.2f)
        {
            y0 = crossing.StartDeckY;
            y1 = crossing.EndDeckY;
            if (y0 < -1000f || y1 < -1000f)
            {
                var (a, b) = ComputeBankHeights(crossing.Path, crossing.StartDist, crossing.EndDist);
                y0 = a;
                y1 = b;
            }
        }

        // Extend past span ends so mesh chunks meeting the bridge stay on grade.
        float fade = 12f;
        float d0 = Mathf.Max(0f, crossing.StartDist - fade);
        float d1 = Mathf.Min(pathLen, crossing.EndDist + fade);

        int changed = 0;
        for (int i = 0; i < points.Length; i++)
        {
            float d = pathLen * i / (points.Length - 1);
            if (d < d0 || d > d1)
                continue;

            Vector3 p = points[i];
            float terrainY = TerrainMeta.HeightMap.GetHeight(p);
            float deckY;

            if (d < crossing.StartDist)
            {
                float t = Mathf.InverseLerp(d0, crossing.StartDist, d);
                deckY = Mathf.Lerp(terrainY, y0, t);
            }
            else if (d > crossing.EndDist)
            {
                float t = Mathf.InverseLerp(d1, crossing.EndDist, d);
                deckY = Mathf.Lerp(terrainY, y1, t);
            }
            else
            {
                float t = Mathf.InverseLerp(crossing.StartDist, crossing.EndDist, d);
                deckY = Mathf.Lerp(y0, y1, t);
            }

            if (Mathf.Abs(p.y - deckY) > 0.01f)
                changed++;
            p.y = deckY;
            points[i] = p;
        }

        crossing.Path.Path.RecalculateTangents();

        if (RoadFixConfig.Config?.DebugLogging == true)
        {
            Debug.Log(
                $"[RoadFix] Elevated path '{crossing.Path.Name}' slope Y {y0:F1}→{y1:F1} " +
                $"span={crossing.StartDist:F0}-{crossing.EndDist:F0} changedPoints={changed}/{points.Length}");
        }
    }

    public static float ComputeDeckY(PathList path, float startDist, float endDist)
    {
        var (y0, y1) = ComputeBankHeights(path, startDist, endDist);
        return (y0 + y1) * 0.5f;
    }

    public static (float startY, float endY) ComputeBankHeights(PathList path, float startDist, float endDist)
    {
        return (SampleAbutment(path, startDist, -1f).y, SampleAbutment(path, endDist, 1f).y);
    }

    /// <summary>
    /// Path Y just outside the wet span — the road/rail the player is actually on.
    /// Do not use water+clearance or max-terrain; those sit ~1m above the mesh.
    /// </summary>
    public static Vector3 SampleAbutment(PathList path, float fromDist, float direction)
    {
        float pathLen = path.Path.Length;
        for (float offset = 6f; offset <= 20f; offset += 2f)
        {
            float d = Mathf.Clamp(fromDist + direction * offset, 0f, pathLen);
            Vector3 pt = SamplePoint(path, d);
            if (!RiverProximity.IsInRiverChannel(pt, padMetres: 0.5f))
                return pt;
        }

        return SamplePoint(path, Mathf.Clamp(fromDist + direction * 8f, 0f, pathLen));
    }

    public static Vector3 SamplePoint(PathList path, float dist)
    {
        dist = Mathf.Clamp(dist, 0f, path.Path.Length);
        return path.Spline ? path.Path.GetPointCubicHermite(dist) : path.Path.GetPoint(dist);
    }

    /// <summary>
    /// Heights for bridge pitch: sample dry approach grade outside each bank
    /// (wider than one node so decks tip enough on hills).
    /// </summary>
    public static void SamplePitchHeights(
        PathList path,
        float startDist,
        float endDist,
        float nodeLength,
        out Vector3 prev,
        out Vector3 next,
        out float yPrev,
        out float yNext)
    {
        prev = SampleAbutment(path, startDist, -1f);
        next = SampleAbutment(path, endDist, 1f);
        yPrev = prev.y;
        yNext = next.y;
    }

    /// <summary>
    /// Road path nodes around the span center (node before / node after).
    /// Heights drive bridge pitch so the deck matches the road grade.
    /// </summary>
    public static void SampleCenterNeighborNodes(
        PathList path,
        float startDist,
        float endDist,
        float nodeLength,
        out Vector3 prev,
        out Vector3 next,
        out float yPrev,
        out float yNext,
        out float centerDist)
    {
        centerDist = (startDist + endDist) * 0.5f;
        float step = Mathf.Max(4f, nodeLength);
        PathInterpolator interp = path.Path;
        Vector3[] pts = interp.Points;

        // Prefer real control-point indices (the 3–4 nodes across a short bridge).
        if (pts != null && pts.Length >= 3 && interp.Length > 0.01f)
        {
            float avgStep = interp.Length / (pts.Length - 1);
            int indexStep = Mathf.Max(1, Mathf.RoundToInt(step / Mathf.Max(0.01f, avgStep)));
            int centerIdx = Mathf.Clamp(
                Mathf.RoundToInt(centerDist / interp.Length * (pts.Length - 1)),
                indexStep,
                pts.Length - 1 - indexStep);

            prev = pts[centerIdx - indexStep];
            next = pts[centerIdx + indexStep];
            yPrev = NodeHeight(prev);
            yNext = NodeHeight(next);

            // Widen to ±2 nodes if the grade is almost flat.
            if (Mathf.Abs(yNext - yPrev) < 0.2f && centerIdx - 2 * indexStep >= 0
                && centerIdx + 2 * indexStep < pts.Length)
            {
                prev = pts[centerIdx - 2 * indexStep];
                next = pts[centerIdx + 2 * indexStep];
                yPrev = NodeHeight(prev);
                yNext = NodeHeight(next);
            }

            return;
        }

        prev = SamplePoint(path, Mathf.Max(0f, centerDist - step));
        next = SamplePoint(path, Mathf.Min(interp.Length, centerDist + step));
        yPrev = NodeHeight(prev);
        yNext = NodeHeight(next);
    }

    private static float NodeHeight(Vector3 pt)
    {
        float pathY = pt.y;
        if (RiverProximity.IsOnRiver(pt, 0f))
            return pathY;

        float ground = TerrainMeta.HeightMap.GetHeight(pt);
        return Mathf.Max(pathY, ground);
    }
}
