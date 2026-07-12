using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CustomMapGen.Patches
{
    internal static class SwapSpawnTracking
    {
        private struct SwapRowSnapshot
        {
            public uint Id;
            public string Path;
            public Vector3 Position;
            public Vector3 RotationEuler;
            public Vector3 Scale;
        }

        private static readonly object Sync = new object();
        private static bool _active;
        private static string _sourceMapName = "";
        private static readonly Dictionary<uint, int> ExpectedCounts = new Dictionary<uint, int>();
        private static readonly Dictionary<uint, int> AttemptedCounts = new Dictionary<uint, int>();
        private static readonly Dictionary<uint, int> NullPrefabCounts = new Dictionary<uint, int>();
        private static readonly List<SwapRowSnapshot> ExpectedRows = new List<SwapRowSnapshot>();
        private static bool _finalizeRequested;
        private static DateTime _finalizeNotBeforeUtc;
        private const float FinalizeDelaySeconds = 20f;
        private static bool _recoveryRequested;
        private static List<SwapRowSnapshot> _pendingRecoveryRows;
        private static float _pendingRecoveryDelaySeconds = 20f;

        internal static void BeginTracking(string sourceMapName, IList createdPrefabs)
        {
            lock (Sync)
            {
                _active = true;
                _sourceMapName = sourceMapName ?? "(unknown)";
                ExpectedCounts.Clear();
                AttemptedCounts.Clear();
                NullPrefabCounts.Clear();
                ExpectedRows.Clear();
                _finalizeRequested = false;
                _finalizeNotBeforeUtc = DateTime.UtcNow.AddSeconds(FinalizeDelaySeconds);
                _recoveryRequested = false;
                _pendingRecoveryRows = null;
                _pendingRecoveryDelaySeconds = 20f;

                if (createdPrefabs == null)
                    return;

                for (int i = 0; i < createdPrefabs.Count; i++)
                {
                    object row = createdPrefabs[i];
                    if (!PostSaveSwap.TryGetPrefabId(row, out uint id) || id == 0)
                        continue;
                    if (ExpectedCounts.TryGetValue(id, out int n))
                        ExpectedCounts[id] = n + 1;
                    else
                        ExpectedCounts[id] = 1;

                    string path = StringPool.Get(id) ?? "";
                    Vector3 pos = GetPrefabVector3(row, "position");
                    Vector3 rot = GetPrefabVector3(row, "rotation");
                    Vector3 scale = GetPrefabVector3(row, "scale");
                    ExpectedRows.Add(new SwapRowSnapshot
                    {
                        Id = id,
                        Path = path,
                        Position = pos,
                        RotationEuler = rot,
                        Scale = scale
                    });
                }
            }
        }

        internal static void RecordSpawnAttempt(Prefab prefab)
        {
            lock (Sync)
            {
                if (!_active || prefab == null)
                    return;

                uint id = prefab.ID;
                if (id == 0 || !ExpectedCounts.ContainsKey(id))
                    return;

                if (AttemptedCounts.TryGetValue(id, out int count))
                    AttemptedCounts[id] = count + 1;
                else
                    AttemptedCounts[id] = 1;

                if (prefab.Object == null)
                {
                    if (NullPrefabCounts.TryGetValue(id, out int nullCount))
                        NullPrefabCounts[id] = nullCount + 1;
                    else
                        NullPrefabCounts[id] = 1;
                }
            }
        }

        internal static void EndTrackingAndLog(string source)
        {
            bool deferFinalize = false;
            lock (Sync)
            {
                if (!_active)
                    return;

                // World.Spawn postfix fires before all spawn calls finish. Request a delayed finalize.
                if (string.Equals(source, "Spawn", StringComparison.OrdinalIgnoreCase))
                {
                    _finalizeRequested = true;
                    deferFinalize = true;
                }
            }

            if (deferFinalize)
                return;

            lock (Sync)
            {
                if (!_active)
                    return;

                _active = false;
                if (ExpectedCounts.Count == 0)
                    return;

                int expectedTotal = 0;
                int attemptedTotal = 0;
                int nullTotal = 0;
                foreach (var kvp in ExpectedCounts) expectedTotal += kvp.Value;
                foreach (var kvp in AttemptedCounts) attemptedTotal += kvp.Value;
                foreach (var kvp in NullPrefabCounts) nullTotal += kvp.Value;

                UnityEngine.Debug.Log(
                    $"[CustomMapGen] [TRACK] Outpost swap spawn coverage ({_sourceMapName}, source={source}): " +
                    $"expectedRows={expectedTotal} uniqueExpectedIds={ExpectedCounts.Count} attemptedRows={attemptedTotal} nullPrefabRows={nullTotal}");

                var missing = new List<uint>();
                foreach (var kvp in ExpectedCounts)
                {
                    if (!AttemptedCounts.ContainsKey(kvp.Key))
                        missing.Add(kvp.Key);
                }

                if (missing.Count == 0)
                {
                    UnityEngine.Debug.Log("[CustomMapGen] [TRACK] Every swapped outpost prefab ID was attempted at least once.");
                }
                else
                {
                    missing.Sort();
                    UnityEngine.Debug.Log($"[CustomMapGen] [TRACK] Missing attempted IDs: {missing.Count} (showing up to 40)");
                    int show = Math.Min(40, missing.Count);
                    for (int i = 0; i < show; i++)
                    {
                        uint id = missing[i];
                        string path = StringPool.Get(id);
                        int expected = ExpectedCounts.TryGetValue(id, out int e) ? e : 0;
                        UnityEngine.Debug.Log($"[CustomMapGen] [TRACK] missing id={id} expectedCount={expected} path=\"{path}\"");
                    }
                }

                var nullIds = new List<uint>(NullPrefabCounts.Keys);
                if (nullIds.Count > 0)
                {
                    nullIds.Sort();
                    UnityEngine.Debug.Log($"[CustomMapGen] [TRACK] Null-prefab IDs during spawn: {nullIds.Count} (showing up to 40)");
                    int show = Math.Min(40, nullIds.Count);
                    for (int i = 0; i < show; i++)
                    {
                        uint id = nullIds[i];
                        string path = StringPool.Get(id);
                        int attempts = AttemptedCounts.TryGetValue(id, out int a) ? a : 0;
                        int nulls = NullPrefabCounts.TryGetValue(id, out int n) ? n : 0;
                        UnityEngine.Debug.Log($"[CustomMapGen] [TRACK] null id={id} attempts={attempts} nullCount={nulls} path=\"{path}\"");
                    }
                }

                var cfg = CustomMapGen.Instance?.GetConfig();
                if (cfg?.SwapMonuments != null && cfg.SwapMonuments.EnableLateEntityRecovery && ExpectedRows.Count > 0)
                {
                    _pendingRecoveryRows = new List<SwapRowSnapshot>(ExpectedRows);
                    _pendingRecoveryDelaySeconds = cfg.SwapMonuments.LateEntityRecoveryDelaySeconds > 0f
                        ? cfg.SwapMonuments.LateEntityRecoveryDelaySeconds
                        : 20f;
                    _recoveryRequested = true;
                    UnityEngine.Debug.Log($"[CustomMapGen] [RECOVER] Scheduled late entity recovery for {_pendingRecoveryRows.Count} swapped rows (delay={_pendingRecoveryDelaySeconds:0.0}s).");
                }
            }
        }

        internal static void PumpMainThread()
        {
            bool shouldFinalize = false;
            bool shouldRecover = false;
            List<SwapRowSnapshot> recoverRows = null;
            float recoverDelay = 20f;
            lock (Sync)
            {
                if (_active && _finalizeRequested && DateTime.UtcNow >= _finalizeNotBeforeUtc)
                {
                    _finalizeRequested = false;
                    shouldFinalize = true;
                }

                if (_recoveryRequested && _pendingRecoveryRows != null && _pendingRecoveryRows.Count > 0)
                {
                    shouldRecover = true;
                    recoverRows = _pendingRecoveryRows;
                    recoverDelay = _pendingRecoveryDelaySeconds;
                    _recoveryRequested = false;
                    _pendingRecoveryRows = null;
                }
            }

            if (shouldFinalize)
                EndTrackingAndLog("SpawnDelayed");

            if (shouldRecover && ServerMgr.Instance != null)
                ServerMgr.Instance.StartCoroutine(LateEntityRecoveryCoroutine(recoverRows, recoverDelay));
        }

        private static IEnumerator LateEntityRecoveryCoroutine(List<SwapRowSnapshot> rows, float delaySeconds)
        {
            if (rows == null || rows.Count == 0)
                yield break;

            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            if (GameManager.server == null)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] [RECOVER] GameManager.server is null; skipping late entity recovery.");
                yield break;
            }

            var existing = BuildExistingEntityIndex();
            int candidates = 0;
            int spawned = 0;
            int skippedExisting = 0;
            int createFailed = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!ShouldAttemptLateRecovery(row.Path))
                    continue;

                candidates++;
                if (HasNearbyExisting(existing, row.Id, row.Position, 1.5f))
                {
                    skippedExisting++;
                    continue;
                }

                BaseEntity entity = null;
                try
                {
                    entity = GameManager.server.CreateEntity(row.Path, row.Position, Quaternion.Euler(row.RotationEuler), true);
                }
                catch (Exception)
                {
                    entity = null;
                }

                if (entity == null)
                {
                    createFailed++;
                    continue;
                }

                entity.Spawn();
                spawned++;
                AddExisting(existing, row.Id, row.Position);
            }

            UnityEngine.Debug.Log($"[CustomMapGen] [RECOVER] Late entity recovery complete: candidates={candidates}, spawned={spawned}, skippedExisting={skippedExisting}, createFailed={createFailed}.");
        }

        private static Dictionary<uint, List<Vector3>> BuildExistingEntityIndex()
        {
            var index = new Dictionary<uint, List<Vector3>>();
            BaseEntity[] entities = UnityEngine.Object.FindObjectsOfType<BaseEntity>();
            if (entities == null || entities.Length == 0)
                return index;

            for (int i = 0; i < entities.Length; i++)
            {
                BaseEntity e = entities[i];
                if (e == null)
                    continue;

                uint id = e.prefabID;
                if (id == 0)
                    continue;

                if (!index.TryGetValue(id, out List<Vector3> list))
                {
                    list = new List<Vector3>();
                    index[id] = list;
                }
                list.Add(e.transform.position);
            }

            return index;
        }

        private static bool HasNearbyExisting(Dictionary<uint, List<Vector3>> index, uint id, Vector3 pos, float radius)
        {
            if (!index.TryGetValue(id, out List<Vector3> list) || list == null || list.Count == 0)
                return false;

            float sqr = radius * radius;
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i] - pos).sqrMagnitude <= sqr)
                    return true;
            }
            return false;
        }

        private static void AddExisting(Dictionary<uint, List<Vector3>> index, uint id, Vector3 pos)
        {
            if (!index.TryGetValue(id, out List<Vector3> list))
            {
                list = new List<Vector3>();
                index[id] = list;
            }
            list.Add(pos);
        }

        private static bool ShouldAttemptLateRecovery(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string p = path.ToLowerInvariant();
            if (p.IndexOf("assets/prefabs/", StringComparison.Ordinal) >= 0)
                return true;
            if (p.IndexOf("/npc/", StringComparison.Ordinal) >= 0)
                return true;
            if (p.IndexOf("/casino/", StringComparison.Ordinal) >= 0)
                return true;
            if (p.IndexOf("/vendingmachine/", StringComparison.Ordinal) >= 0)
                return true;
            if (p.IndexOf("/card table/", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        private static Vector3 GetPrefabVector3(object prefab, string memberName)
        {
            object vec = PostSaveSwap.GetPrefabMember(prefab, memberName);
            if (vec == null)
                return Vector3.zero;

            return new Vector3(
                GetVectorComponent(vec, "x"),
                GetVectorComponent(vec, "y"),
                GetVectorComponent(vec, "z"));
        }

        private static float GetVectorComponent(object vectorObj, string axisName)
        {
            if (vectorObj == null)
                return 0f;

            var type = vectorObj.GetType();
            var field = type.GetField(axisName);
            if (field != null)
            {
                object value = field.GetValue(vectorObj);
                if (value != null)
                    return Convert.ToSingle(value);
            }

            var prop = type.GetProperty(axisName);
            if (prop != null)
            {
                object value = prop.GetValue(vectorObj);
                if (value != null)
                    return Convert.ToSingle(value);
            }

            return 0f;
        }
    }
}
