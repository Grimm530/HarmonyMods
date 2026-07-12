using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace Convoy
{
    /// <summary>
    /// TerrainPath.Roads / Monuments are internal fields on the game type — property lookup always fails.
    /// Mirror CustomMapGen: FieldInfo with NonPublic.
    /// </summary>
    public static class TerrainPathAccessor
    {
        private static FieldInfo _roadsField;
        private static FieldInfo _monumentsField;
        private static IList _roads;
        private static IEnumerable _monuments;

        private static FieldInfo GetField(string name)
        {
            return typeof(TerrainPath).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        public static IList GetRoads()
        {
            if (TerrainMeta.Path == null) return null;
            if (_roads != null) return _roads;
            if (_roadsField == null) _roadsField = GetField("Roads");
            _roads = _roadsField?.GetValue(TerrainMeta.Path) as IList;
            return _roads;
        }

        public static IEnumerable GetMonuments()
        {
            if (TerrainMeta.Path == null) return null;
            if (_monuments != null) return _monuments;
            if (_monumentsField == null) _monumentsField = GetField("Monuments");
            _monuments = _monumentsField?.GetValue(TerrainMeta.Path) as IEnumerable;
            return _monuments;
        }

        /// <summary>Clear cached lists (call when unloading or after map change).</summary>
        public static void ClearCache()
        {
            _roads = null;
            _monuments = null;
        }

        public static PathList GetRoadAt(int index)
        {
            var roads = GetRoads();
            if (roads == null || index < 0 || index >= roads.Count) return null;
            return roads[index] as PathList;
        }

        public static int IndexOfRoad(PathList road)
        {
            var roads = GetRoads();
            if (roads == null || road == null) return -1;
            return roads.IndexOf(road);
        }
    }
    public class EventPath
    {
        public readonly List<PathPoint> Points = new List<PathPoint>();
        public readonly HashSet<int> IncludedRoadIndexes = new HashSet<int>();
        public bool IsRoundRoad;
        public PathPoint StartPathPoint;
        public Vector3 SpawnRotation;

        public EventPath(bool isRoundRoad)
        {
            IsRoundRoad = isRoundRoad;
        }
    }

    public class PathPoint
    {
        public Vector3 Position;
        public readonly List<PathPoint> ConnectedPoints = new List<PathPoint>();
        public bool Disabled;
        public readonly int RoadIndex;
        public float LastVisitTime;

        public PathPoint(Vector3 position, int roadIndex)
        {
            Position = position;
            RoadIndex = roadIndex;
        }

        public void ConnectPoint(PathPoint pathPoint)
        {
            if (pathPoint != null && !ConnectedPoints.Contains(pathPoint))
                ConnectedPoints.Add(pathPoint);
        }
    }

    public class RoadMonumentData
    {
        public string MonumentName;
        public List<Vector3> LocalPathPoints;
        public Vector3 MonumentSize;
        public HashSet<MonumentInfo> Monuments;
    }

    public static class PositionDefiner
    {
        public static Vector3 GetGlobalPosition(Transform parentTransform, Vector3 position)
        {
            return parentTransform != null ? parentTransform.TransformPoint(position) : position;
        }

        public static Vector3 GetLocalPosition(Transform parentTransform, Vector3 globalPosition)
        {
            return parentTransform != null ? parentTransform.InverseTransformPoint(globalPosition) : globalPosition;
        }

        public static bool GetNavmeshInPoint(Vector3 position, float radius, out NavMeshHit navMeshHit)
        {
            return NavMesh.SamplePosition(position, out navMeshHit, radius, NavMesh.AllAreas);
        }
    }

    /// <summary>Pathfinding for convoy routes. Requires ConvoyPathManager.ConfigProvider to be set (e.g. from ConvoyMod).</summary>
    public static class ConvoyPathManager
    {
        public static EventPath CurrentPath { get; private set; }

        /// <summary>Set by ConvoyMod on load so pathfinding can read config.</summary>
        public static Func<ConvoyPluginConfig> ConfigProvider { get; set; }

        /// <summary>Base directory for custom route JSON files (e.g. HarmonyConfig).</summary>
        public static string CustomRoutesBaseDir { get; set; }

        private static ConvoyPluginConfig Cfg => ConfigProvider?.Invoke();

        private static readonly List<RoadMonumentData> RoadMonuments = new List<RoadMonumentData>
        {
            new RoadMonumentData
            {
                MonumentName = "assets/bundled/prefabs/autospawn/monument/roadside/radtown_1.prefab",
                LocalPathPoints = new List<Vector3>
                {
                    new Vector3(-44.502f, 0, -0.247f), new Vector3(-37.827f, 0, -3.054f), new Vector3(-31.451f, 0, -4.384f),
                    new Vector3(-24.0621f, 0, -7.598f), new Vector3(-14.619f, 0, -5.652f), new Vector3(-7.505f, 0, -0.728f),
                    new Vector3(4.770f, 0, -0.499f), new Vector3(13.913f, 0, 2.828f), new Vector3(18.432f, 0, 4.635f),
                    new Vector3(23.489f, 0, 3.804f), new Vector3(32.881f, 0, -4.063f), new Vector3(47f, 0, -0.293f)
                },
                MonumentSize = new Vector3(49.2f, 0, 11f),
                Monuments = new HashSet<MonumentInfo>()
            }
        };

        public static void StartCachingRoutes()
        {
            var monuments = TerrainPathAccessor.GetMonuments();
            if (monuments == null) return;
            foreach (var data in RoadMonuments)
            {
                data.Monuments = new HashSet<MonumentInfo>(monuments.Cast<MonumentInfo>().Where(x => x != null && x.name == data.MonumentName));
            }
            if (Cfg?.PathConfig == null) return;
            if (Cfg.PathConfig.PathType == 1)
                ConvoyComplexPathGenerator.StartCachingPaths();
        }

        public static void GenerateNewPath()
        {
            CurrentPath = null;
            var roads = TerrainPathAccessor.GetRoads();
            int roadCount = roads?.Count ?? 0;
            if (roadCount == 0)
            {
                UnityEngine.Debug.LogWarning("[Convoy] RouteNotFound: TerrainMeta.Path.Roads is empty or inaccessible (need a map with roads).");
                ConvoyNotifyStub.PrintError(null, "RouteNotFound_Exeption");
                return;
            }

            if (Cfg?.PathConfig == null)
            {
                CurrentPath = ConvoyRegularPathGenerator.GetRegularPath();
                if (CurrentPath != null)
                {
                    CurrentPath.StartPathPoint = DefineStartPoint(CurrentPath);
                    CurrentPath.SpawnRotation = DefineSpawnRotation(CurrentPath);
                }
                if (CurrentPath == null || CurrentPath.StartPathPoint == null)
                {
                    CurrentPath = null;
                    UnityEngine.Debug.LogWarning("[Convoy] RouteNotFound: no suitable road (roads=" + roadCount + ", no PathConfig).");
                    ConvoyNotifyStub.PrintError(null, "RouteNotFound_Exeption");
                }
                return;
            }

            if (Cfg.PathConfig.PathType == 1)
                CurrentPath = ConvoyComplexPathGenerator.GetRandomPath();
            else if (Cfg.PathConfig.PathType == 2)
                CurrentPath = ConvoyCustomPathGenerator.GetCustomPath();

            if (CurrentPath == null)
                CurrentPath = ConvoyRegularPathGenerator.GetRegularPath();

            if (CurrentPath != null)
            {
                CurrentPath.StartPathPoint = DefineStartPoint(CurrentPath);
                CurrentPath.SpawnRotation = DefineSpawnRotation(CurrentPath);
            }

            if (CurrentPath == null || CurrentPath.StartPathPoint == null)
            {
                CurrentPath = null;
                UnityEngine.Debug.LogWarning("[Convoy] RouteNotFound: PathType=" + Cfg.PathConfig.PathType
                    + " MinRoadLength=" + Cfg.PathConfig.MinRoadLength
                    + " roads=" + roadCount
                    + " (complex cache may still be building — try PathType 0).");
                ConvoyNotifyStub.PrintError(null, "RouteNotFound_Exeption");
            }
            else
            {
                UnityEngine.Debug.Log("[Convoy] Route ready: " + CurrentPath.Points.Count + " points, ring=" + CurrentPath.IsRoundRoad
                    + ", start=" + CurrentPath.StartPathPoint.Position);
            }
        }

        private static int GetRoadIndex(PathList road)
        {
            return TerrainPathAccessor.IndexOfRoad(road);
        }

        private static bool IsRoadRound(Vector3[] points)
        {
            if (points == null || points.Length < 2) return false;
            return Vector3.Distance(points[0], points[points.Length - 1]) < 5f;
        }

        private static PathPoint DefineStartPoint(EventPath path)
        {
            if (path == null || path.Points.Count == 0) return null;
            PathPoint newStartPoint;
            NavMeshHit navMeshHit;

            if (path.IsRoundRoad)
            {
                // Prefer navmesh-snapped points, but road points often sit off humanoid navmesh — do not fail the route.
                var candidates = path.Points.Where(x => PositionDefiner.GetNavmeshInPoint(x.Position, 8, out navMeshHit)).ToList();
                newStartPoint = candidates.Count > 0 ? candidates.GetRandom() : path.Points.GetRandom();
            }
            else
            {
                var ends = path.Points.Where(x => x.ConnectedPoints.Count == 1 && !IsPointSpearfishingVillage(x.Position)).ToList();
                newStartPoint = ends.Count > 0 ? ends.GetRandom() : path.Points[0];
            }

            if (newStartPoint == null) newStartPoint = path.Points[0];

            // Snap to navmesh when available (NPC dismount); otherwise keep road point and fix Y from heightmap.
            if (PositionDefiner.GetNavmeshInPoint(newStartPoint.Position, 12, out navMeshHit))
                newStartPoint.Position = navMeshHit.position;
            else if (TerrainMeta.HeightMap != null)
            {
                Vector3 p = newStartPoint.Position;
                p.y = TerrainMeta.HeightMap.GetHeight(p);
                newStartPoint.Position = p;
            }
            return newStartPoint;
        }

        private static bool IsPointSpearfishingVillage(Vector3 position)
        {
            var monuments = TerrainPathAccessor.GetMonuments();
            return monuments != null && monuments.Cast<MonumentInfo>().Any(x => x != null && x.name != null && x.name.Contains("fishing") && Vector3.Distance(position, x.transform.position) < 75);
        }

        private static Vector3 DefineSpawnRotation(EventPath path)
        {
            if (path?.StartPathPoint == null || path.StartPathPoint.ConnectedPoints.Count == 0) return Vector3.forward;
            for (int i = 0; i < path.StartPathPoint.ConnectedPoints.Count; i++)
            {
                if (i == 0)
                {
                    path.StartPathPoint.ConnectedPoints[i].Disabled = false;
                }
                else
                {
                    path.StartPathPoint.ConnectedPoints[i].Disabled = true;
                }
            }
            PathPoint secondPoint = path.StartPathPoint.ConnectedPoints[0];
            return (secondPoint.Position - path.StartPathPoint.Position).normalized;
        }

        public static void OnSpawnFinish()
        {
            if (CurrentPath?.StartPathPoint?.ConnectedPoints == null) return;
            foreach (var p in CurrentPath.StartPathPoint.ConnectedPoints)
                p.Disabled = false;
        }

        public static void OnPluginUnloaded()
        {
            ConvoyComplexPathGenerator.StopPathGenerating();
            TerrainPathAccessor.ClearCache();
            CurrentPath = null;
        }

        public static MonumentInfo GetRoadMonumentInPosition(Vector3 position)
        {
            foreach (var data in RoadMonuments)
            {
                if (data.Monuments == null) continue;
                foreach (var m in data.Monuments)
                {
                    if (m == null || m.transform == null) continue;
                    Vector3 local = PositionDefiner.GetLocalPosition(m.transform, position);
                    if (Math.Abs(local.x) < data.MonumentSize.x && Math.Abs(local.z) < data.MonumentSize.z)
                        return m;
                }
            }
            return null;
        }

        public static void TryContinuePathThrough(MonumentInfo monument, Vector3 position, int roadIndex, ref PathPoint previousPoint, ref EventPath eventPath)
        {
            var data = RoadMonuments.FirstOrDefault(x => x.MonumentName == monument.name);
            if (data == null || data.LocalPathPoints == null || data.LocalPathPoints.Count == 0) return;
            Vector3 startGlobal = PositionDefiner.GetGlobalPosition(monument.transform, data.LocalPathPoints[0]);
            Vector3 endGlobal = PositionDefiner.GetGlobalPosition(monument.transform, data.LocalPathPoints[data.LocalPathPoints.Count - 1]);

            if (Vector3.Distance(position, startGlobal) < Vector3.Distance(position, endGlobal))
            {
                var monumentStart = new PathPoint(startGlobal, roadIndex);
                if (previousPoint != null) { monumentStart.ConnectPoint(previousPoint); previousPoint.ConnectPoint(monumentStart); }
                previousPoint = monumentStart;
                foreach (var local in data.LocalPathPoints)
                {
                    var global = PositionDefiner.GetGlobalPosition(monument.transform, local);
                    var pp = new PathPoint(global, roadIndex);
                    pp.ConnectPoint(previousPoint);
                    previousPoint.ConnectPoint(pp);
                    eventPath.Points.Add(pp);
                    previousPoint = pp;
                }
            }
            else
            {
                var monumentStart = new PathPoint(endGlobal, roadIndex);
                if (previousPoint != null) { monumentStart.ConnectPoint(previousPoint); previousPoint.ConnectPoint(monumentStart); }
                previousPoint = monumentStart;
                for (int i = data.LocalPathPoints.Count - 1; i >= 0; i--)
                {
                    var global = PositionDefiner.GetGlobalPosition(monument.transform, data.LocalPathPoints[i]);
                    var pp = new PathPoint(global, roadIndex);
                    pp.ConnectPoint(previousPoint);
                    previousPoint.ConnectPoint(pp);
                    eventPath.Points.Add(pp);
                    previousPoint = pp;
                }
            }
        }

        internal static int GetRoadIndexInternal(PathList road) => GetRoadIndex(road);
    }

    internal static class ConvoyRegularPathGenerator
    {
        private static bool IsRoadRound(Vector3[] points) => points != null && points.Length >= 2 && Vector3.Distance(points[0], points[points.Length - 1]) < 5f;

        public static EventPath GetRegularPath()
        {
            var roads = TerrainPathAccessor.GetRoads();
            if (roads == null || roads.Count == 0) return null;
            var cfg = ConvoyPathManager.ConfigProvider?.Invoke()?.PathConfig;
            int minLen = cfg?.MinRoadLength ?? 200;
            var blockRoads = cfg?.BlockRoads ?? new HashSet<int>();
            bool preferRing = cfg?.RegularPathConfig?.IsRingRoad ?? true;

            PathList road = null;
            if (preferRing)
                road = roads.Cast<PathList>().FirstOrDefault(x => !blockRoads.Contains(TerrainPathAccessor.IndexOfRoad(x)) && IsRoadRound(x.Path.Points) && x.Path.Length > minLen);
            if (road == null)
            {
                var suitable = roads.Cast<PathList>().Where(x => !blockRoads.Contains(TerrainPathAccessor.IndexOfRoad(x)) && x.Path.Length > minLen).ToList();
                road = suitable.Count > 0 ? suitable.GetRandom() : null;
            }
            if (road == null) return null;
            return GetPathFromRegularRoad(road, minLen, blockRoads);
        }

        private static EventPath GetPathFromRegularRoad(PathList road, int minRoadLength, HashSet<int> blockRoads)
        {
            bool isRound = IsRoadRound(road.Path.Points);
            var path = new EventPath(isRound);
            int roadIndex = TerrainPathAccessor.IndexOfRoad(road);
            PathPoint previousPoint = null;
            bool isOnMonument = false;

            foreach (Vector3 position in road.Path.Points)
            {
                if (position.y < 0 && !isRound) break;
                if (isOnMonument)
                {
                    if (ConvoyPathManager.GetRoadMonumentInPosition(position) == null) isOnMonument = false;
                    else continue;
                }
                var monument = ConvoyPathManager.GetRoadMonumentInPosition(position);
                if (monument != null)
                {
                    isOnMonument = true;
                    ConvoyPathManager.TryContinuePathThrough(monument, position, roadIndex, ref previousPoint, ref path);
                    continue;
                }
                var newPoint = new PathPoint(position, roadIndex);
                if (previousPoint != null) { newPoint.ConnectPoint(previousPoint); previousPoint.ConnectPoint(newPoint); }
                path.Points.Add(newPoint);
                previousPoint = newPoint;
            }
            if (isRound && path.Points.Count >= 2)
            {
                path.IsRoundRoad = true;
                var first = path.Points.First();
                var last = path.Points.Last();
                first.ConnectPoint(last);
                last.ConnectPoint(first);
            }
            return path;
        }
    }

    internal static class ConvoyComplexPathGenerator
    {
        private static bool _isGenerationFinished;
        private static readonly List<EventPath> _complexPaths = new List<EventPath>();
        private static Coroutine _cachingCoroutine;
        private static readonly HashSet<Vector3> EndPoints = new HashSet<Vector3>();

        public static EventPath GetRandomPath()
        {
            if (!_isGenerationFinished || _complexPaths.Count == 0) return null;
            var cfg = ConvoyPathManager.ConfigProvider?.Invoke()?.PathConfig?.ComplexPathConfig;
            if (cfg != null && cfg.ChooseLongestRoute && _complexPaths.Count > 0)
                return _complexPaths.OrderByDescending(x => x.IncludedRoadIndexes.Count).First();
            return _complexPaths.GetRandom();
        }

        public static void StartCachingPaths()
        {
            EndPoints.Clear();
            var roads = TerrainPathAccessor.GetRoads();
            if (roads != null)
            {
                foreach (PathList road in roads)
                {
                    if (road?.Path?.Points != null && road.Path.Points.Length > 0)
                    {
                        EndPoints.Add(road.Path.Points[0]);
                        EndPoints.Add(road.Path.Points[road.Path.Points.Length - 1]);
                    }
                }
            }
            _cachingCoroutine = ServerMgr.Instance?.StartCoroutine(CachingCoroutine());
        }

        public static void StopPathGenerating()
        {
            if (_cachingCoroutine != null && ServerMgr.Instance != null)
            {
                ServerMgr.Instance.StopCoroutine(_cachingCoroutine);
                _cachingCoroutine = null;
            }
        }

        private static IEnumerator CachingCoroutine()
        {
            ConvoyNotifyStub.PrintLogMessage("RouteCachingStart_Log");
            _complexPaths.Clear();
            var cfg = ConvoyPathManager.ConfigProvider?.Invoke()?.PathConfig;
            int minLen = cfg?.MinRoadLength ?? 200;
            var blockRoads = cfg?.BlockRoads ?? new HashSet<int>();
            int minRoadCount = cfg?.ComplexPathConfig?.MinRoadCount ?? 3;

            var roadsList = TerrainPathAccessor.GetRoads();
            if (roadsList == null) yield break;
            for (int roadIndex = 0; roadIndex < roadsList.Count; roadIndex++)
            {
                if (blockRoads.Contains(roadIndex)) continue;
                var roadPathList = roadsList[roadIndex] as PathList;
                if (roadPathList == null) continue;
                if (roadPathList.Path.Length < minLen) continue;
                _complexPaths.Add(new EventPath(false));
                yield return CachingRoad(roadIndex, 0, -1, blockRoads, minLen, roadsList);
            }
            EndPoints.Clear();
            _complexPaths.RemoveAll(p => p == null || (p.IncludedRoadIndexes != null && p.IncludedRoadIndexes.Count < minRoadCount));
            _isGenerationFinished = true;
            ConvoyNotifyStub.PrintWarningMessage("RouteCachingStop_Log", _complexPaths.Count);
        }

        private static IEnumerator CachingRoad(int roadIndex, int startPointIndex, int pathPointForConnectionIndex, HashSet<int> blockRoads, int minRoadLength, IList roadsList)
        {
            if (_complexPaths.Count == 0 || roadsList == null) yield break;
            EventPath path = _complexPaths[_complexPaths.Count - 1];
            path.IncludedRoadIndexes.Add(roadIndex);
            PathList road = roadsList[roadIndex] as PathList;
            if (road == null) yield break;
            PathPoint pointForConnection = pathPointForConnectionIndex >= 0 && pathPointForConnectionIndex < path.Points.Count ? path.Points[pathPointForConnectionIndex] : null;
            bool isOnMonument = false;

            for (int pointIndex = startPointIndex + 1; pointIndex < road.Path.Points.Length; pointIndex++)
            {
                Vector3 position = road.Path.Points[pointIndex];
                if (position.y < 0) break;
                if (isOnMonument) { if (ConvoyPathManager.GetRoadMonumentInPosition(position) == null) isOnMonument = false; else continue; }
                MonumentInfo monument = ConvoyPathManager.GetRoadMonumentInPosition(position);
                if (monument != null) { isOnMonument = true; ConvoyPathManager.TryContinuePathThrough(monument, position, roadIndex, ref pointForConnection, ref path); continue; }
                pointForConnection = CachingPoint(roadIndex, pointIndex, pointForConnection, path, road, blockRoads, minRoadLength, roadsList, out _);
                if (pointIndex % 50 == 0) yield return null;
            }
            pointForConnection = pathPointForConnectionIndex >= 0 && pathPointForConnectionIndex < path.Points.Count ? path.Points[pathPointForConnectionIndex] : null;
            isOnMonument = false;
            for (int pointIndex = startPointIndex - 1; pointIndex >= 0; pointIndex--)
            {
                Vector3 position = road.Path.Points[pointIndex];
                if (position.y < 0) break;
                if (isOnMonument) { if (ConvoyPathManager.GetRoadMonumentInPosition(position) == null) isOnMonument = false; else continue; }
                MonumentInfo monument2 = ConvoyPathManager.GetRoadMonumentInPosition(position);
                if (monument2 != null) { isOnMonument = true; ConvoyPathManager.TryContinuePathThrough(monument2, position, roadIndex, ref pointForConnection, ref path); continue; }
                pointForConnection = CachingPoint(roadIndex, pointIndex, pointForConnection, path, road, blockRoads, minRoadLength, roadsList, out var pathConnectedData);
                if (pathConnectedData != null && !path.IncludedRoadIndexes.Contains(pathConnectedData.NewRoadIndex))
                {
                    Vector3 currentRoadPoint = road.Path.Points[pathConnectedData.PathPointIndex];
                    PathList newRoadPathList = roadsList[pathConnectedData.NewRoadIndex] as PathList;
                    if (newRoadPathList != null)
                    {
                        Vector3 newRoadPoint = newRoadPathList.Path.Points.OrderBy(x => Vector3.Distance(x, currentRoadPoint)).First();
                        int indexForStart = Array.IndexOf(newRoadPathList.Path.Points, newRoadPoint);
                        yield return CachingRoad(pathConnectedData.NewRoadIndex, indexForStart, pathConnectedData.PointForConnectionIndex, blockRoads, minRoadLength, roadsList);
                    }
                }
                if (pointIndex % 50 == 0) yield return null;
            }
        }

        private static PathPoint CachingPoint(int roadIndex, int pointIndex, PathPoint lastPathPoint, EventPath eventPath, PathList road, HashSet<int> blockRoads, int minRoadLength, IList roadsList, out PathConnectedData pathConnectedData)
        {
            pathConnectedData = null;
            Vector3 point = road.Path.Points[pointIndex];
            var newPathPoint = new PathPoint(point, roadIndex);
            if (lastPathPoint != null) { newPathPoint.ConnectPoint(lastPathPoint); lastPathPoint.ConnectPoint(newPathPoint); }
            eventPath.Points.Add(newPathPoint);

            if (roadsList != null)
            {
                if (pointIndex == 0 || pointIndex == road.Path.Points.Length - 1)
                {
                    var newRoad = roadsList.Cast<PathList>().FirstOrDefault(x => !blockRoads.Contains(TerrainPathAccessor.IndexOfRoad(x)) && x.Path.Length > minRoadLength && !eventPath.IncludedRoadIndexes.Contains(TerrainPathAccessor.IndexOfRoad(x)) && (Vector3.Distance(x.Path.Points[0], point) < 7.5f || Vector3.Distance(x.Path.Points[x.Path.Points.Length - 1], point) < 7.5f));
                    if (newRoad != null)
                        pathConnectedData = new PathConnectedData { PathPointIndex = pointIndex, NewRoadIndex = TerrainPathAccessor.IndexOfRoad(newRoad), PointForConnectionIndex = eventPath.Points.IndexOf(newPathPoint) };
                }
                if (EndPoints.Any(x => Vector3.Distance(x, point) < 7.5f))
                {
                    var newRoad = roadsList.Cast<PathList>().FirstOrDefault(x => x != road && !blockRoads.Contains(TerrainPathAccessor.IndexOfRoad(x)) && x.Path.Length > minRoadLength && !eventPath.IncludedRoadIndexes.Contains(TerrainPathAccessor.IndexOfRoad(x)) && x.Path.Points.Any(y => Vector3.Distance(y, point) < 7.5f));
                    if (newRoad != null)
                        pathConnectedData = new PathConnectedData { PathPointIndex = pointIndex, NewRoadIndex = TerrainPathAccessor.IndexOfRoad(newRoad), PointForConnectionIndex = eventPath.Points.IndexOf(newPathPoint) };
                }
            }
            return newPathPoint;
        }

        private class PathConnectedData
        {
            public int PathPointIndex;
            public int NewRoadIndex;
            public int PointForConnectionIndex;
        }
    }

    internal static class ConvoyCustomPathGenerator
    {
        public static EventPath GetCustomPath()
        {
            var cfg = ConvoyPathManager.ConfigProvider?.Invoke()?.PathConfig?.CustomPathConfig?.CustomRoutesPresets;
            if (cfg == null || cfg.Count == 0) return null;
            string pathName = cfg.GetRandom();
            if (string.IsNullOrEmpty(pathName)) return null;
            string baseDir = ConvoyPathManager.CustomRoutesBaseDir ?? "HarmonyConfig";
            string filePath = System.IO.Path.Combine(baseDir, "Convoy", "Custom routes", pathName + ".json");
            if (!System.IO.File.Exists(filePath))
            {
                filePath = System.IO.Path.Combine("Convoy", "Custom routes", pathName + ".json");
                if (!System.IO.File.Exists(filePath)) return null;
            }
            try
            {
                var customRouteData = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomRouteData>(System.IO.File.ReadAllText(filePath));
                if (customRouteData?.Points == null || customRouteData.Points.Count == 0) return null;
                return GetCaravanPathFromCustomRouteData(customRouteData);
            }
            catch { return null; }
        }

        private static EventPath GetCaravanPathFromCustomRouteData(CustomRouteData data)
        {
            var points = data.Points.Select(s => s.ToVector3()).Where(p => p != Vector3.zero).ToList();
            if (points.Count == 0) return null;
            var path = new EventPath(false);
            PathPoint previousPoint = null;
            foreach (Vector3 position in points)
            {
                if (!PositionDefiner.GetNavmeshInPoint(position, 2, out _)) return null;
                var newPoint = new PathPoint(position, -1);
                if (previousPoint != null) { newPoint.ConnectPoint(previousPoint); previousPoint.ConnectPoint(newPoint); }
                path.Points.Add(newPoint);
                previousPoint = newPoint;
            }
            return path;
        }
    }

    public class CustomRouteData
    {
        public List<string> Points { get; set; }
    }
}
