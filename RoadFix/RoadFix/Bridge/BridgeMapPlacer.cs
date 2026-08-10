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

    public static int PlaceCrossing(BridgeCrossing crossing, string mapPath, Vector3 pathCenterLocal)
    {
        var cfg = RoadFixConfig.Config;
        if (cfg == null || string.IsNullOrEmpty(mapPath))
            return 0;

        string fullPath = Path.IsPathRooted(mapPath) ? mapPath : Path.GetFullPath(mapPath);
        if (!File.Exists(fullPath))
        {
            Debug.LogWarning($"[RoadFix] Bridge map not found: {fullPath}");
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

        // 1) Place level (yaw only) with path-center local node on the road center node.
        GetYawPlacement(crossing, pathCenterLocal, cfg, pathDir,
            out Vector3 mapOriginWorld, out Quaternion yawRotation, out Vector3 pivotWorld);

        float lengthScale = GetNodeBasedLengthScale(crossing, cfg);

        Vector3 yawEuler = yawRotation.eulerAngles;
        object startPos = NewVector(mapOriginWorld.x, mapOriginWorld.y, mapOriginWorld.z);
        object startRot = NewVector(yawEuler.x, yawEuler.y, yawEuler.z);
        IList created = CreatePrefabFromMap(startPos, startRot, templatePrefabs);
        if (created == null || created.Count == 0)
            return 0;

        ApplyLengthScale(created, mapOriginWorld, yawRotation, lengthScale, lengthOnX);

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
            Debug.Log(
                $"[RoadFix] Queued bridge from {Path.GetFileName(fullPath)} at {crossing.Center} " +
                $"span={crossing.SpanLength:F1} nodes={crossing.NodeCount} lengthScale={lengthScale:F2} " +
                $"nodeY={yPrev:F1}→{yNext:F1} pitch={pitchDeg:F1}° pivot={pivotWorld} " +
                $"heightOffset={cfg.BridgeHeightOffset} yaw={cfg.BridgeYawOffset} axis={cfg.BridgeLengthAxis} " +
                $"serialized={serialized} deferred={deferred} skipped={skipped}");
        }

        return serialized;
    }

    /// <summary>Flattened StartDist → EndDist direction (stable across spans).</summary>
    private static Vector3 GetPathDirection(BridgeCrossing crossing)
    {
        Vector3 p0 = BridgeTerrain.SamplePoint(crossing.Path, crossing.StartDist);
        Vector3 p1 = BridgeTerrain.SamplePoint(crossing.Path, crossing.EndDist);
        Vector3 flat = new Vector3(p1.x - p0.x, 0f, p1.z - p0.z);
        if (flat.sqrMagnitude < 0.001f)
        {
            flat = crossing.Tangent;
            flat.y = 0f;
        }
        if (flat.sqrMagnitude < 0.001f)
            flat = Vector3.forward;
        return flat.normalized;
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
        // Place on road/rail path node height (same as before the bank-average experiment).
        pivotWorld = BridgeTerrain.SamplePoint(crossing.Path, mid);
        pivotWorld.y += cfg.BridgeHeightOffset;

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
    /// Base BridgeLengthScale, then +StretchPerExtraNode only from the 4th node onward.
    /// </summary>
    private static float GetNodeBasedLengthScale(BridgeCrossing crossing, RoadFixConfig.ConfigData cfg)
    {
        float scale = Mathf.Max(0.1f, cfg.BridgeLengthScale);
        int threshold = Mathf.Max(1, cfg.StretchOnlyAfterNodes);
        int nodes = Mathf.Max(1, crossing.NodeCount);
        if (nodes > threshold)
            scale += (nodes - threshold) * Mathf.Max(0f, cfg.StretchPerExtraNode);
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
