using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RoadFix.Bridge;

/// <summary>
/// Places a RustEdit .map bridge at a path crossing. Map origin is (0,0,0); we put the
/// configured path-center local node onto the world path point so Bridgeonly offsets stay correct.
/// </summary>
internal static class BridgeMapPlacer
{
    private static readonly Assembly GameAssembly = typeof(World).Assembly;
    private static Type _vectorDataType;
    private static Type _prefabDataType;
    private static readonly Dictionary<string, IList> PrefabCache = new Dictionary<string, IList>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingMapsLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TemplateMetrics> MetricsCache =
        new Dictionary<string, TemplateMetrics>(StringComparer.OrdinalIgnoreCase);

    private struct TemplateMetrics
    {
        public float NativeWidth;
        public float NativeLength;
        public float GravelTopLocalY;
        public bool HasGravel;
        // Where the authored gravel deck is centered in local space.
        // We use this (x/z) so placements don't depend on the RustEdit node being exact.
        public Vector3 DeckCenterLocal;
    }

    public static int PlaceCrossing(BridgeCrossing crossing, string mapPath, Vector3 pathCenterLocal)
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null || string.IsNullOrEmpty(mapPath))
            return 0;

        string fullPath = Path.IsPathRooted(mapPath) ? mapPath : Path.GetFullPath(mapPath);
        if (!File.Exists(fullPath))
        {
            if (MissingMapsLogged.Add(fullPath))
            {
                Debug.LogError(
                    $"[RoadFix] Bridge map not found: {fullPath}. " +
                    "Put bridge.map in maps/prefabs (or set RoadBridgeMapPath).");
            }
            return 0;
        }

        IList templatePrefabs = LoadPrefabs(fullPath);
        if (templatePrefabs == null || templatePrefabs.Count == 0)
        {
            Debug.LogWarning($"[RoadFix] Bridge map has no prefabs: {fullPath}");
            return 0;
        }

        bool lengthOnX = !string.Equals(cfg.BridgeLengthAxis, "Z", System.StringComparison.OrdinalIgnoreCase);

        // One path direction for yaw + pitch: StartDist → EndDist (avoids flipped spans).
        Vector3 pathDir = GetPathDirection(crossing);

        TemplateMetrics metrics = GetMetrics(fullPath, pathCenterLocal, lengthOnX);

        // Centered tile: config path-center is (0,5,0). Do not use old Bridgeonly XYZ.
        GetYawPlacement(crossing, pathCenterLocal, cfg, pathDir,
            out Vector3 mapOriginWorld, out Quaternion yawRotation, out Vector3 pivotWorld);

        float lengthScale = GetLengthScale(crossing, metrics, cfg);

        Vector3 yawEuler = yawRotation.eulerAngles;
        object startPos = NewVector(mapOriginWorld.x, mapOriginWorld.y, mapOriginWorld.z);
        object startRot = NewVector(yawEuler.x, yawEuler.y, yawEuler.z);
        IList created = CreatePrefabFromMap(startPos, startRot, templatePrefabs);
        if (created == null || created.Count == 0)
            return 0;

        // Scale length around the path-center pivot so the deck stays on the road.
        ApplyLengthScale(created, pivotWorld, yawRotation, lengthScale, lengthOnX);
        float nativeClear = NativeClearWidth(crossing, cfg);
        float widthScale = GetWidthScale(crossing, cfg, nativeClear);
        ApplyWidthScale(created, pivotWorld, yawRotation, widthScale, lengthOnX);

        // 2) Pitch around center using node heights, axis = bridge length (after yaw).
        float pitchDeg = ComputeNodePitch(crossing, cfg, pathDir, yawRotation, lengthOnX,
            out float yPrev, out float yNext);
        if (Mathf.Abs(pitchDeg) >= 0.05f)
            ApplyPitchAroundPivot(created, pivotWorld, yawRotation, lengthOnX, pitchDeg);

        int serialized = 0;
        int deferred = 0;
        int skipped = 0;
        foreach (object row in created)
        {
            if (row == null || !TryGetPrefabId(row, out uint id) || id == 0)
                continue;

            string path = StringPool.Get(id);
            if (string.IsNullOrEmpty(path))
            {
                skipped++;
                if (cfg.DebugLogging)
                    Debug.LogWarning($"[RoadFix] Skipping unknown bridge prefab id={id}");
                continue;
            }

            if (ShouldSkipBridgePrefab(path))
            {
                skipped++;
                if (cfg.DebugLogging)
                    Debug.Log($"[RoadFix] Skipping non-bridge prefab in map: {path}");
                continue;
            }

            string category = Convert.ToString(GetMember(row, "category"));
            if (string.IsNullOrEmpty(category))
                category = "Decor";

            Vector3 rowPos = GetVector3(row, "position");
            Quaternion rowRot = Quaternion.Euler(GetVector3(row, "rotation"));
            Vector3 rowScale = GetVector3(row, "scale");
            if (rowScale == Vector3.zero)
                rowScale = Vector3.one;

            // Persist into the .map now; live-spawn after AssetScene-props loads.
            World.Serialization?.AddPrefab(category, id, rowPos, rowRot, rowScale);
            DeferredBridgeSpawn.Enqueue(category, id, path, rowPos, rowRot, rowScale);
            serialized++;
            deferred++;
        }

        if (cfg.DebugLogging)
        {
            Vector3 pathMid = BridgeTerrain.SamplePoint(crossing.Path, (crossing.StartDist + crossing.EndDist) * 0.5f);
            float terrainMid = TerrainMeta.HeightMap.GetHeight(pathMid);
            Debug.Log(
                $"[RoadFix] Queued bridge from {Path.GetFileName(fullPath)} at {crossing.Center} " +
                $"span={crossing.SpanLength:F1} nodes={crossing.NodeCount} nativeLen={metrics.NativeLength:F1} " +
                $"lengthScale={lengthScale:F2} " +
                $"abutmentY={yPrev:F2}→{yNext:F2} pitch={pitchDeg:F1}° pivot={pivotWorld} " +
                $"pathMidY={pathMid.y:F2} pivotY={pivotWorld.y:F2} terrainMidY={terrainMid:F2} " +
                $"banks={crossing.StartDeckY:F2}→{crossing.EndDeckY:F2} bedDetect={crossing.RiverBedY:F2} " +
                $"heightOffset={cfg.BridgeHeightOffset} pathLocal={pathCenterLocal} yawOff={cfg.BridgeYawOffset} yaw={yawEuler.y:F1} head={Mathf.Atan2(pathDir.x, pathDir.z) * Mathf.Rad2Deg:F1} " +
                $"axis={cfg.BridgeLengthAxis} nativeClear={nativeClear:F1} " +
                $"coverWidth={crossing.CoverWidth:F1} widthScale={widthScale:F2} extras={crossing.ExtraSpans?.Count ?? 0} " +
                $"serialized={serialized} deferred={deferred} skipped={skipped}");
        }

        return serialized;
    }

    /// <summary>
    /// Heading of the path through the river, matching the mesh centerline.
    /// Bank-approach tangents must not be mixed in — a straight rail/road in the
    /// water with curved approaches was yawing the tile clockwise.
    /// </summary>
    private static Vector3 GetPathDirection(BridgeCrossing crossing)
    {
        Vector3 sum = Vector3.zero;
        AddSpanHeading(crossing.Path, crossing.StartDist, crossing.EndDist, ref sum);
        if (crossing.ExtraSpans != null)
        {
            for (int i = 0; i < crossing.ExtraSpans.Count; i++)
            {
                BridgeCrossing extra = crossing.ExtraSpans[i];
                AddSpanHeading(extra.Path, extra.StartDist, extra.EndDist, ref sum);
            }
        }

        if (sum.sqrMagnitude > 0.0001f)
            return sum.normalized;

        Vector3 stored = crossing.Tangent;
        stored.y = 0f;
        if (stored.sqrMagnitude > 0.001f)
            return stored.normalized;

        return Vector3.forward;
    }

    /// <summary>
    /// Short chord at mid-span (cubic if the path is a spline). Same positions the
    /// rail/road mesh uses. Window is capped so long water-expands do not pick up
    /// bank curves.
    /// </summary>
    private static void AddSpanHeading(PathList path, float start, float end, ref Vector3 sum)
    {
        if (path?.Path == null)
            return;

        float pathLen = path.Path.Length;
        float mid = (start + end) * 0.5f;
        float half = Mathf.Min(6f, Mathf.Max(2f, (end - start) * 0.15f));
        float d0 = Mathf.Clamp(mid - half, 0f, pathLen);
        float d1 = Mathf.Clamp(mid + half, 0f, pathLen);
        Vector3 a = path.Spline ? path.Path.GetPointCubicHermite(d0) : path.Path.GetPoint(d0);
        Vector3 b = path.Spline ? path.Path.GetPointCubicHermite(d1) : path.Path.GetPoint(d1);
        Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
        if (flat.sqrMagnitude > 0.0001f)
            sum += flat.normalized;
    }

    /// <summary>Yaw-only placement: pathCenterLocal sits on the road center node (pivot).</summary>
    private static void GetYawPlacement(
        BridgeCrossing crossing,
        Vector3 pathCenterLocal,
        RoadFixConfig.ConfigData cfg,
        Vector3 pathDir,
        out Vector3 mapOriginWorld,
        out Quaternion yawRotation,
        out Vector3 pivotWorld)
    {
        float mid = (crossing.StartDist + crossing.EndDist) * 0.5f;
        Vector3 midPt = BridgeTerrain.SamplePoint(crossing.Path, mid);
        Vector3 startAbut = BridgeTerrain.SampleAbutment(crossing.Path, crossing.StartDist, -1f);
        Vector3 endAbut = BridgeTerrain.SampleAbutment(crossing.Path, crossing.EndDist, 1f);
        // Mid-span path Y is the river hump (water+2). Ends must match the approach road.
        pivotWorld = midPt;
        if (crossing.CoverWidth > 1f)
        {
            pivotWorld.x = crossing.Center.x;
            pivotWorld.z = crossing.Center.z;
        }
        pivotWorld.y = (startAbut.y + endAbut.y) * 0.5f + cfg.BridgeHeightOffset;

        yawRotation = Quaternion.LookRotation(pathDir, Vector3.up)
            * Quaternion.Euler(0f, cfg.BridgeYawOffset, 0f);

        // Map origin so pathCenterLocal lands on pivot (rotate-around-center).
        mapOriginWorld = pivotWorld - yawRotation * pathCenterLocal;
    }

    private static float ComputeNodePitch(
        BridgeCrossing crossing,
        RoadFixConfig.ConfigData cfg,
        Vector3 pathDir,
        Quaternion yawRotation,
        bool lengthOnX,
        out float yPrev,
        out float yNext)
    {
        BridgeTerrain.SamplePitchHeights(
            crossing.Path, crossing.StartDist, crossing.EndDist,
            Mathf.Max(4f, cfg.BridgeTemplateLength),
            out Vector3 prev, out Vector3 next, out yPrev, out yNext);

        float horiz = Mathf.Max(0.001f, Vector3.Distance(
            new Vector3(prev.x, 0f, prev.z),
            new Vector3(next.x, 0f, next.z)));
        float maxPitch = Mathf.Max(0f, cfg.MaxBridgePitchDegrees);
        float pitchDeg = Mathf.Atan2(yNext - yPrev, horiz) * Mathf.Rad2Deg;

        // If yaw+offset points length opposite Start→End, invert so the high bank still wins.
        Vector3 lengthAxis = GetLengthAxis(yawRotation, lengthOnX);
        if (Vector3.Dot(lengthAxis, pathDir) < 0f)
            pitchDeg = -pitchDeg;

        float sign = cfg.BridgePitchSign >= 0f ? 1f : -1f;
        pitchDeg *= sign;

        return Mathf.Clamp(pitchDeg, -maxPitch, maxPitch);
    }

    private static Vector3 GetLengthAxis(Quaternion yawRotation, bool lengthOnX)
    {
        Vector3 axis = lengthOnX ? yawRotation * Vector3.right : yawRotation * Vector3.forward;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.001f)
            axis = Vector3.forward;
        return axis.normalized;
    }

    /// <summary>
    /// Rotate every cube around the road/path center as one rigid assembly.
    /// Pitch axis is perpendicular to bridge length (not an independent node chord).
    /// </summary>
    private static void ApplyPitchAroundPivot(
        IList rows,
        Vector3 pivotWorld,
        Quaternion yawRotation,
        bool lengthOnX,
        float pitchDeg)
    {
        Vector3 lengthAxis = GetLengthAxis(yawRotation, lengthOnX);
        Vector3 pitchAxis = Vector3.Cross(Vector3.up, lengthAxis).normalized;
        if (pitchAxis.sqrMagnitude < 0.001f)
            pitchAxis = Vector3.right;

        Quaternion pitchQ = Quaternion.AngleAxis(pitchDeg, pitchAxis);

        foreach (object row in rows)
        {
            if (row == null)
                continue;

            Vector3 pos = GetVector3(row, "position");
            pos = pivotWorld + pitchQ * (pos - pivotWorld);
            SetMember(row, "position", NewVector(pos.x, pos.y, pos.z));

            Vector3 rotE = GetVector3(row, "rotation");
            Quaternion rot = pitchQ * Quaternion.Euler(rotE);
            Vector3 e = rot.eulerAngles;
            SetMember(row, "rotation", NewVector(e.x, e.y, e.z));
        }
    }

    private static bool ShouldSkipBridgePrefab(string path)
    {
        string p = path.ToLowerInvariant();
        return p.Contains("/npc/")
            || p.Contains("/agents/")
            || p.Contains("lumberjack")
            || p.Contains("scientist")
            || p.Contains("murderer")
            || p.Contains("/player/")
            || p.Contains("autospawn/monument")
            || p.Contains("/vehicles/")
            || p.Contains("modularcar");
    }

    /// <summary>
    /// Size the tile to the measured water span plus overhang past each bank.
    /// Stretches when the river is wider than the template; still floors on creeks.
    /// </summary>
    private static float GetLengthScale(
        BridgeCrossing crossing,
        TemplateMetrics metrics,
        RoadFixConfig.ConfigData cfg)
    {
        float native = metrics.NativeLength > 1f
            ? metrics.NativeLength
            : Mathf.Max(4f, cfg.BridgeTemplateLength);
        float overhangEach = Mathf.Max(0f, cfg.BridgeLengthOverhang);
        float target = Mathf.Max(8f, crossing.SpanLength + overhangEach * 2f);
        float fitScale = target / native;
        float minScale = Mathf.Clamp(cfg.MinBridgeLengthScale, 0.2f, 1f);
        float maxScale = Mathf.Clamp(cfg.MaxBridgeLengthScale, 1f, 6f);
        float scale = Mathf.Clamp(fitScale, minScale, maxScale);
        if (cfg.DebugLogging)
        {
            Debug.Log(
                $"[RoadFix] Length water={crossing.SpanLength:F1} +{overhangEach:F0}m/bank " +
                $"target={target:F1} nativeLen={native:F1} → lengthScale={scale:F2} " +
                $"path='{crossing.Path?.Name}'");
        }
        return scale;
    }

    private static void ApplyLengthScale(IList rows, Vector3 origin, Quaternion rotation, float lengthScale, bool lengthOnX)
    {
        if (Mathf.Abs(lengthScale - 1f) < 0.01f)
            return;

        Quaternion inv = Quaternion.Inverse(rotation);
        foreach (object row in rows)
        {
            Vector3 pos = GetVector3(row, "position");
            Vector3 local = inv * (pos - origin);
            if (lengthOnX) local.x *= lengthScale;
            else local.z *= lengthScale;
            Vector3 world = origin + rotation * local;
            SetMember(row, "position", NewVector(world.x, world.y, world.z));

            Vector3 scale = GetVector3(row, "scale");
            if (scale == Vector3.zero)
                scale = Vector3.one;
            if (lengthOnX) scale.x *= lengthScale;
            else scale.z *= lengthScale;
            SetMember(row, "scale", NewVector(scale.x, scale.y, scale.z));
        }
    }

    /// <summary>
    /// World Y of the gravel driving surface at each abutment (cube_tiled_gravel top).
    /// </summary>
    public static void GetRailDeckGrade(BridgeCrossing crossing, out float y0, out float y1)
    {
        y0 = 0f;
        y1 = 0f;
        var cfg = RoadFixConfig.Config;
        if (cfg == null || crossing.Path?.Path == null)
            return;

        Vector3 startAbut = BridgeTerrain.SampleAbutment(crossing.Path, crossing.StartDist, -1f);
        Vector3 endAbut = BridgeTerrain.SampleAbutment(crossing.Path, crossing.EndDist, 1f);
        bool lengthOnX = !string.Equals(cfg.BridgeLengthAxis, "Z", StringComparison.OrdinalIgnoreCase);
        Vector3 pathLocal = cfg.SharedPathCenterLocal;
        string mapPath = cfg.SharedBridgeMapPath;
        string fullPath = Path.IsPathRooted(mapPath) ? mapPath : Path.GetFullPath(mapPath);
        TemplateMetrics metrics = GetMetrics(fullPath, pathLocal, lengthOnX);

        float topOffset = metrics.GravelTopLocalY - pathLocal.y;
        float extra = cfg.RailDeckGravelOffset;
        y0 = startAbut.y + cfg.BridgeHeightOffset + topOffset + extra;
        y1 = endAbut.y + cfg.BridgeHeightOffset + topOffset + extra;
    }

    private static float NativeClearWidth(BridgeCrossing crossing, RoadFixConfig.ConfigData cfg)
    {
        bool road = crossing.Path != null && crossing.Path.Width >= 8f;
        float native = road ? cfg.RoadBridgeNativeWidth : cfg.RailBridgeNativeWidth;
        return Mathf.Max(4f, native);
    }

    private static float GetWidthScale(BridgeCrossing crossing, RoadFixConfig.ConfigData cfg, float nativeClear)
    {
        if (cfg == null || crossing.CoverWidth <= 1f)
            return 1f;

        float native = Mathf.Max(4f, nativeClear);
        float need = crossing.CoverWidth;
        if (need <= native + 0.25f)
            return 1f;

        float maxScale = Mathf.Clamp(cfg.MaxRailBridgeWidthScale, 1f, 5f);
        float scale = Mathf.Clamp(need / native, 1f, maxScale);
        if (cfg.DebugLogging)
        {
            Debug.Log(
                $"[RoadFix] widthScale={scale:F2} cover={need:F1} nativeClear={native:F1} " +
                $"path='{crossing.Path?.Name}' width={crossing.Path?.Width:F1}");
        }
        return scale;
    }

    private static void ApplyWidthScale(IList rows, Vector3 origin, Quaternion rotation, float widthScale, bool lengthOnX)
    {
        if (Mathf.Abs(widthScale - 1f) < 0.01f)
            return;

        Quaternion inv = Quaternion.Inverse(rotation);
        foreach (object row in rows)
        {
            Vector3 pos = GetVector3(row, "position");
            Vector3 local = inv * (pos - origin);
            if (lengthOnX) local.z *= widthScale;
            else local.x *= widthScale;
            Vector3 world = origin + rotation * local;
            SetMember(row, "position", NewVector(world.x, world.y, world.z));

            Vector3 scale = GetVector3(row, "scale");
            if (scale == Vector3.zero)
                scale = Vector3.one;
            if (lengthOnX) scale.z *= widthScale;
            else scale.x *= widthScale;
            SetMember(row, "scale", NewVector(scale.x, scale.y, scale.z));
        }
    }

    private static TemplateMetrics GetMetrics(string fullPath, Vector3 pathCenterLocal, bool lengthOnX)
    {
        if (MetricsCache.TryGetValue(fullPath, out TemplateMetrics cached))
            return cached;

        IList prefabs = File.Exists(fullPath) ? LoadPrefabs(fullPath) : null;
        TemplateMetrics metrics = MeasureTemplate(prefabs, pathCenterLocal, lengthOnX);
        if (prefabs != null)
            MetricsCache[fullPath] = metrics;
        return metrics;
    }

    private static TemplateMetrics MeasureTemplate(IList prefabs, Vector3 pathCenterLocal, bool lengthOnX)
    {
        var metrics = new TemplateMetrics
        {
            NativeWidth = 8f,
            NativeLength = 12f,
            GravelTopLocalY = pathCenterLocal.y,
            HasGravel = false,
            DeckCenterLocal = pathCenterLocal
        };
        if (prefabs == null || prefabs.Count == 0)
            return metrics;

        float minW = float.MaxValue;
        float maxW = float.MinValue;
        float minL = float.MaxValue;
        float maxL = float.MinValue;
        float gravelTop = float.MinValue;

        foreach (object row in prefabs)
        {
            if (row == null || !TryGetPrefabId(row, out uint id) || id == 0)
                continue;
            Vector3 pos = GetVector3(row, "position");
            Vector3 scale = GetVector3(row, "scale");
            if (scale == Vector3.zero)
                scale = Vector3.one;

            string path = StringPool.Get(id);
            if (!string.IsNullOrEmpty(path) && ShouldSkipBridgePrefab(path))
                continue;

            float across = lengthOnX ? pos.z : pos.x;
            float halfW = 0.5f * Mathf.Abs(lengthOnX ? scale.z : scale.x);
            minW = Mathf.Min(minW, across - halfW);
            maxW = Mathf.Max(maxW, across + halfW);

            float along = lengthOnX ? pos.x : pos.z;
            float halfL = 0.5f * Mathf.Abs(lengthOnX ? scale.x : scale.z);
            minL = Mathf.Min(minL, along - halfL);
            maxL = Mathf.Max(maxL, along + halfL);

            if (string.IsNullOrEmpty(path))
                continue;
            string lower = path.ToLowerInvariant();
            if (lower.IndexOf("cube_tiled_gravel", StringComparison.Ordinal) < 0)
                continue;

            metrics.HasGravel = true;
            float top = pos.y + 0.5f * Mathf.Abs(scale.y);
            if (top > gravelTop)
                gravelTop = top;
        }

        if (maxW > minW && minW < float.MaxValue * 0.5f)
            metrics.NativeWidth = maxW - minW;
        if (maxL > minL && minL < float.MaxValue * 0.5f)
            metrics.NativeLength = maxL - minL;

        // Align deck center (x/z) based on gravel bounds.
        // This prevents consistent lateral offsets when RustEdit node offsets change.
        if (maxW > minW && maxL > minL && minW < float.MaxValue * 0.5f && minL < float.MaxValue * 0.5f)
        {
            float centerW = (minW + maxW) * 0.5f;
            float centerL = (minL + maxL) * 0.5f;
            metrics.DeckCenterLocal = lengthOnX
                ? new Vector3(centerL, pathCenterLocal.y, centerW)
                : new Vector3(centerW, pathCenterLocal.y, centerL);
        }
        if (metrics.HasGravel && gravelTop > float.MinValue * 0.5f)
            metrics.GravelTopLocalY = gravelTop;

        return metrics;
    }

    private static IList LoadPrefabs(string fullPath)
    {
        if (PrefabCache.TryGetValue(fullPath, out IList cached))
            return cached;

        try
        {
            var serialization = new WorldSerialization();
            serialization.Load(fullPath);
            object world = GetWorldFromSerialization(serialization);
            IList prefabs = GetPrefabsListFromWorld(world);
            if (prefabs != null)
                PrefabCache[fullPath] = prefabs;
            return prefabs;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RoadFix] Failed to load bridge map '{fullPath}': {ex}");
            return null;
        }
    }

    private static object GetWorldFromSerialization(WorldSerialization serialization)
    {
        if (serialization == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var t = serialization.GetType();
        var prop = t.GetProperty("world", flags) ?? t.GetProperty("World", flags);
        if (prop != null) return prop.GetValue(serialization);
        var field = t.GetField("world", flags) ?? t.GetField("World", flags);
        return field?.GetValue(serialization);
    }

    private static IList GetPrefabsListFromWorld(object world)
    {
        if (world == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var t = world.GetType();
        foreach (string name in new[] { "prefabs", "Prefabs" })
        {
            var prop = t.GetProperty(name, flags);
            if (prop?.GetValue(world) is IList list && list.Count >= 0)
                return list;
            var field = t.GetField(name, flags);
            if (field?.GetValue(world) is IList list2)
                return list2;
        }
        return null;
    }

    private static IList CreatePrefabFromMap(object startPos, object startRot, IList prefabs)
    {
        if (VectorDataType == null || PrefabDataType == null || prefabs == null || prefabs.Count == 0)
            return null;

        object referencePos = NewVector(0f, 0f, 0f);
        var result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(PrefabDataType));
        foreach (var prefab in prefabs)
        {
            object idObj = GetMember(prefab, "id");
            uint id = idObj != null ? Convert.ToUInt32(idObj) : 0u;
            object scale = GetMember(prefab, "scale") ?? NewVector(1f, 1f, 1f);
            object position = Calculate(startPos, GetMember(prefab, "position"), referencePos, startRot);
            // Quaternion multiply — euler-component add warps children when pitch/roll != 0.
            object rotation = CalculateRot(startRot, GetMember(prefab, "rotation"));
            object category = GetMember(prefab, "category") ?? "Decor";
            object newPrefab = NewPrefabData(id, position, rotation, scale, category);
            if (newPrefab != null)
                result.Add(newPrefab);
        }
        return result;
    }

    private static Type VectorDataType => _vectorDataType ??= ResolveType("VectorData");
    private static Type PrefabDataType => _prefabDataType ??= ResolveType("PrefabData");

    private static Type ResolveType(string typeName)
    {
        foreach (var t in GameAssembly.GetTypes())
        {
            if (t.Name == typeName) return t;
        }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == typeName) return t;
                }
            }
            catch
            {
                // ignore dynamic/reflection-only assemblies
            }
        }
        return null;
    }

    private static object Calculate(object globalPos, object position, object referencePos, object firstPrefabRotation)
    {
        object localPos = CalculateLocalPos(referencePos, position, firstPrefabRotation);
        return NewVector(
            GetV(globalPos, "x") + GetV(localPos, "x"),
            GetV(globalPos, "y") + GetV(localPos, "y"),
            GetV(globalPos, "z") + GetV(localPos, "z"));
    }

    private static object CalculateLocalPos(object placePos, object globalPos, object rotation)
    {
        float dx = GetV(globalPos, "x") - GetV(placePos, "x");
        float dy = GetV(globalPos, "y") - GetV(placePos, "y");
        float dz = GetV(globalPos, "z") - GetV(placePos, "z");
        return RotateVector(NewVector(dx, dy, dz), rotation);
    }

    private static object RotateVector(object vector, object rotation)
    {
        Vector3 v = new Vector3(GetV(vector, "x"), GetV(vector, "y"), GetV(vector, "z"));
        Quaternion q = Quaternion.Euler(GetV(rotation, "x"), GetV(rotation, "y"), GetV(rotation, "z"));
        Vector3 r = q * v;
        return NewVector(r.x, r.y, r.z);
    }

    private static object CalculateRot(object globalRot, object localRot)
    {
        Quaternion g = Quaternion.Euler(GetV(globalRot, "x"), GetV(globalRot, "y"), GetV(globalRot, "z"));
        Quaternion l = localRot == null
            ? Quaternion.identity
            : Quaternion.Euler(GetV(localRot, "x"), GetV(localRot, "y"), GetV(localRot, "z"));
        Vector3 e = (g * l).eulerAngles;
        return NewVector(e.x, e.y, e.z);
    }

    private static object NewPrefabData(uint id, object position, object rotation, object scale, object category)
    {
        if (PrefabDataType == null) return null;
        var p = Activator.CreateInstance(PrefabDataType);
        SetMember(p, "id", id);
        SetMember(p, "position", position);
        SetMember(p, "rotation", rotation);
        SetMember(p, "scale", scale);
        SetMember(p, "category", category);
        return p;
    }

    private static object NewVector(float x, float y, float z)
    {
        if (VectorDataType == null) return null;
        var v = Activator.CreateInstance(VectorDataType);
        SetMember(v, "x", x);
        SetMember(v, "y", y);
        SetMember(v, "z", z);
        return v;
    }

    private static object GetMember(object obj, string name)
    {
        if (obj == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var t = obj.GetType();
        var prop = t.GetProperty(name, flags);
        if (prop != null) return prop.GetValue(obj);
        return t.GetField(name, flags)?.GetValue(obj);
    }

    private static void SetMember(object obj, string name, object value)
    {
        if (obj == null) return;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var t = obj.GetType();
        var prop = t.GetProperty(name, flags);
        if (prop != null) { prop.SetValue(obj, value); return; }
        t.GetField(name, flags)?.SetValue(obj, value);
    }

    private static float GetV(object v, string f)
    {
        if (v == null) return 0f;
        var field = v.GetType().GetField(f);
        return field != null ? Convert.ToSingle(field.GetValue(v)) : 0f;
    }

    private static Vector3 GetVector3(object row, string name)
    {
        object v = GetMember(row, name);
        return new Vector3(GetV(v, "x"), GetV(v, "y"), GetV(v, "z"));
    }

    private static bool TryGetPrefabId(object row, out uint id)
    {
        id = 0;
        object idObj = GetMember(row, "id");
        if (idObj == null) return false;
        id = Convert.ToUInt32(idObj);
        return id != 0;
    }
}
