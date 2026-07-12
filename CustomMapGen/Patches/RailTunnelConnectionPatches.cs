using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// After GenerateDungeonGrid runs, tunnel entrances exist in DungeonGridEntrances and rails exist in Path.Rails.
    /// This postfix adds rail segments from the nearest point on the rail network to each tunnel entrance so
    /// above-ground tracks connect visually to the entrance (fixes tracks running past without connecting).
    /// </summary>
    [HarmonyPatch(typeof(GenerateDungeonGrid), nameof(GenerateDungeonGrid.Process))]
    public static class GenerateDungeonGrid_Process_RailTunnelConnection_Postfix
    {
        private const int MaxPathDepth = 250000;
        private const float RailSegmentMinLengthSq = 25f * 25f; // skip if entrance is already very close to rail

        static void Postfix(uint seed)
        {
            if (World.Networked || World.Cached)
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
                return;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config == null || !config.ConnectRailsToTunnelEntrances)
                return;
            if (!World.Config.AboveGroundRails)
                return;
            var rails = TerrainPathAccess.GetRails(TerrainMeta.Path);
            var dungeonEntrances = TerrainPathAccess.GetDungeonGridEntrances(TerrainMeta.Path);
            if (rails == null || rails.Count == 0)
                return;
            if (dungeonEntrances == null || dungeonEntrances.Count == 0)
                return;

            int length = (int)((float)World.Size / 7.5f);
            int[,] costmap = TerrainPath.CreateRailCostmap(ref seed);
            var pathFinder = new PathFinder(costmap);
            var points = new List<Vector3>();
            int added = 0;

            foreach (var entrance in dungeonEntrances)
            {
                if (entrance == null || entrance.transform == null)
                    continue;

                Vector3 entrancePos = entrance.transform.position;
                entrancePos.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(entrancePos), 1f);

                if (!FindClosestRailPoint(rails, entrancePos, out Vector3 closestRailPoint, out _))
                    continue;

                if ((entrancePos - closestRailPoint).sqrMagnitude < RailSegmentMinLengthSq)
                    continue;

                PathFinder.Point startPoint = PathFinder.GetPoint(closestRailPoint, length);
                PathFinder.Point endPoint = PathFinder.GetPoint(entrancePos, length);

                PathFinder.Node startNode = pathFinder.FindClosestWalkable(startPoint, 5);
                PathFinder.Node endNode = pathFinder.FindClosestWalkable(endPoint, 5);
                if (startNode == null || endNode == null)
                    continue;

                PathFinder.Node path = pathFinder.FindPath(startNode.point, endNode.point, MaxPathDepth);
                if (path == null)
                    continue;

                points.Clear();
                points.Add(closestRailPoint);
                for (PathFinder.Node n = path; n != null; n = n.next)
                {
                    float normX = ((float)n.point.x + 0.5f) / length;
                    float normZ = ((float)n.point.y + 0.5f) / length;
                    float x = TerrainMeta.DenormalizeX(normX);
                    float z = TerrainMeta.DenormalizeZ(normZ);
                    float y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(normX, normZ), 1f);
                    points.Add(new Vector3(x, y, z));
                }
                if (points.Count < 2)
                    continue;

                PathList segment = CreateRailSegment(rails.Count + added, points.ToArray());
                segment.Start = false;
                segment.End = true;
                segment.ProcgenStartNode = null;
                segment.ProcgenEndNode = null;

                float Filter(int i)
                {
                    float a = Mathf.InverseLerp(0f, 8f, i);
                    float b = Mathf.InverseLerp(segment.Path.DefaultMaxIndex, segment.Path.DefaultMaxIndex - 8, i);
                    return Mathf.SmoothStep(0f, 1f, Mathf.Min(a, b));
                }
                segment.Path.Smoothen(32, new Vector3(1f, 0f, 1f), Filter);
                segment.Path.Smoothen(64, new Vector3(0f, 1f, 0f), Filter);
                segment.Path.Resample(7.5f);
                segment.Path.RecalculateTangents();
                segment.AdjustPlacementMap(20f);

                rails.Add(segment);
                added++;
            }

            if (config.DebugLogging && added > 0)
                UnityEngine.Debug.Log($"[CustomMapGen] Connected {added} rail segment(s) to tunnel entrances.");
        }

        private static bool FindClosestRailPoint(List<PathList> rails, Vector3 worldPos, out Vector3 closestPoint, out float closestDistSq)
        {
            closestPoint = worldPos;
            closestDistSq = float.MaxValue;
            foreach (var rail in rails)
            {
                if (rail?.Path?.Points == null)
                    continue;
                foreach (Vector3 p in rail.Path.Points)
                {
                    float d2 = (worldPos - p).sqrMagnitude;
                    if (d2 < closestDistSq)
                    {
                        closestDistSq = d2;
                        closestPoint = p;
                    }
                }
            }
            return closestDistSq < float.MaxValue;
        }

        private static PathList CreateRailSegment(int number, Vector3[] points)
        {
            var segment = new PathList("Rail " + number, points)
            {
                Spline = true,
                Width = 4f,
                InnerPadding = 1f,
                OuterPadding = 1f,
                InnerFade = 1f,
                OuterFade = 32f,
                RandomScale = 1f,
                MeshOffset = 0f,
                TerrainOffset = -0.125f,
                Topology = 524288,
                Splat = 128,
                Hierarchy = 1
            };
            return segment;
        }
    }
}
