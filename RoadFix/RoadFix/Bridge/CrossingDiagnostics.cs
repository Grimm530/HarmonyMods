using System.Text;
using UnityEngine;

namespace RoadFix.Bridge;

/// <summary>
/// Verbose per-crossing dumps for tuning terrain carve / bridge height.
/// </summary>
internal static class CrossingDiagnostics
{
    public static void LogCrossing(
        string phase,
        BridgeCrossing crossing,
        PathList river,
        float riverDist,
        float widthHalf,
        float alongHalf,
        float bedTargetY)
    {
        if (RoadFixConfig.Config?.DebugLogging != true || crossing.Path?.Path == null)
            return;

        bool isRail = TerrainMeta.Path?.Rails != null && TerrainMeta.Path.Rails.Contains(crossing.Path);
        PathList path = crossing.Path;
        var sb = new StringBuilder(2048);

        sb.AppendLine(
            $"[RoadFix] === CROSSING {phase} {(isRail ? "RAIL" : "ROAD")} '{path.Name}' " +
            $"hier={path.Hierarchy} span={crossing.StartDist:F1}-{crossing.EndDist:F1} " +
            $"len={crossing.SpanLength:F1}m nodes≈{crossing.NodeCount} ===");
        sb.AppendLine(
            $"  center=({crossing.Center.x:F2},{crossing.Center.y:F2},{crossing.Center.z:F2}) " +
            $"tangent=({crossing.Tangent.x:F2},{crossing.Tangent.z:F2}) " +
            $"pathWidth={path.Width:F1} spline={path.Spline}");
        sb.AppendLine(
            $"  banks StartDeckY={crossing.StartDeckY:F2} EndDeckY={crossing.EndDeckY:F2} " +
            $"DeckY={crossing.DeckY:F2} RiverBedY(detect)={crossing.RiverBedY:F2}");
        sb.AppendLine(
            $"  carve widthHalf={widthHalf:F2} alongHalf={alongHalf:F2} bedTargetY={bedTargetY:F2} " +
            $"heightOffset={RoadFixConfig.Config.BridgeHeightOffset:F2}");

        // Path samples through span + pad
        float pad = 16f;
        float d0 = Mathf.Max(0f, crossing.StartDist - pad);
        float d1 = Mathf.Min(path.Path.Length, crossing.EndDist + pad);
        float step = Mathf.Max(2f, (d1 - d0) / 24f);
        sb.AppendLine($"  --- path samples d={d0:F0}..{d1:F0} step={step:F1} ---");
        for (float d = d0; d <= d1 + 0.01f; d += step)
        {
            Vector3 pt = BridgeTerrain.SamplePoint(path, d);
            float terrainY = TerrainMeta.HeightMap.GetHeight(pt);
            float waterY = WaterLevel.RaycastWaterColliders(pt);
            string tag = d < crossing.StartDist ? "approach" : d > crossing.EndDist ? "exit" : "SPAN";
            sb.AppendLine(
                $"    [{tag}] d={d:F1} pos=({pt.x:F1},{pt.y:F2},{pt.z:F1}) " +
                $"terrainY={terrainY:F2} waterY={waterY:F2} path-terrain={pt.y - terrainY:F2}");
        }

        // Actual path Points array indices overlapping span
        if (path.Path.Points != null && path.Path.Points.Length > 0)
        {
            sb.AppendLine($"  --- path.Points ({path.Path.Points.Length}) in/near span ---");
            float acc = 0f;
            Vector3[] pts = path.Path.Points;
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0)
                    acc += Vector3.Distance(pts[i - 1], pts[i]);
                if (acc < crossing.StartDist - pad || acc > crossing.EndDist + pad)
                    continue;
                Vector3 p = pts[i];
                float terrainY = TerrainMeta.HeightMap.GetHeight(p);
                string tag = acc < crossing.StartDist ? "approach" : acc > crossing.EndDist ? "exit" : "SPAN";
                sb.AppendLine(
                    $"    [{tag}] i={i} d≈{acc:F1} pos=({p.x:F1},{p.y:F2},{p.z:F1}) terrainY={terrainY:F2}");
            }
        }

        if (river?.Path != null)
        {
            float rLen = river.Path.Length;
            float baseR = river.Width * 0.5f;
            float offset = river.TerrainOffset != 0f ? river.TerrainOffset : -1.5f;
            float rad = PathList.GetRadius(riverDist, rLen, baseR, river.RandomScale, true);
            float depth = PathList.GetDepth(riverDist, rLen, offset, river.RandomScale, true);
            float depthUnscaled = offset;
            sb.AppendLine(
                $"  --- river '{river.Name}' d={riverDist:F1}/{rLen:F1} Width={river.Width:F1} " +
                $"TerrainOffset={offset:F2} GetRadius={rad:F2} GetDepth(scaled)={depth:F2} " +
                $"depthUnscaled={depthUnscaled:F2} ---");

            float r0 = Mathf.Max(0f, riverDist - alongHalf - 12f);
            float r1 = Mathf.Min(rLen, riverDist + alongHalf + 12f);
            float rStep = Mathf.Max(3f, (r1 - r0) / 16f);
            for (float d = r0; d <= r1 + 0.01f; d += rStep)
            {
                Vector3 pt = BridgeTerrain.SamplePoint(river, d);
                float terrainY = TerrainMeta.HeightMap.GetHeight(pt);
                float r = PathList.GetRadius(d, rLen, baseR, river.RandomScale, true);
                float dep = PathList.GetDepth(d, rLen, offset, river.RandomScale, false);
                sb.AppendLine(
                    $"    river d={d:F1} pos=({pt.x:F1},{pt.y:F2},{pt.z:F1}) " +
                    $"terrainY={terrainY:F2} radius={r:F1} bed≈{pt.y + dep:F2} " +
                    $"fillAboveBed={terrainY - (pt.y + dep):F2}");
            }
        }

        // Lateral bank probes at mid-span (matches printpos style checks)
        Vector3 mid = BridgeTerrain.SamplePoint(path, (crossing.StartDist + crossing.EndDist) * 0.5f);
        Vector3 tan = crossing.Tangent;
        tan.y = 0f;
        if (tan.sqrMagnitude > 0.0001f)
        {
            tan.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, tan).normalized;
            sb.AppendLine("  --- lateral probes at mid-span (side of path) ---");
            foreach (float s in new[] { -alongHalf, -alongHalf * 0.5f, 0f, alongHalf * 0.5f, alongHalf })
            {
                Vector3 p = mid + side * s;
                float ty = TerrainMeta.HeightMap.GetHeight(p);
                sb.AppendLine($"    side={s:F0}m pos=({p.x:F1},{ty:F2},{p.z:F1}) terrainY={ty:F2}");
            }
        }

        Debug.Log(sb.ToString());
    }
}
