using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Outpost.map rows that need AssetScene-props cannot spawn during PlaceMonuments.
    /// Queue them during live swap, then spawn LAST after map gen reaches DONE.
    /// (ServerMgr does not exist yet at DONE, so work runs on a temporary runner after the server is up.)
    /// </summary>
    internal static class DeferredOutpostSpawn
    {
        internal struct DeferredRow
        {
            public string Category;
            public uint Id;
            public string Path;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private static readonly List<DeferredRow> Pending = new List<DeferredRow>();
        private static bool _doneWorkStarted;

        internal static void Clear()
        {
            Pending.Clear();
            _doneWorkStarted = false;
        }

        internal static void Enqueue(string category, uint id, string path, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Pending.Add(new DeferredRow
            {
                Category = string.IsNullOrEmpty(category) ? "Monument" : category,
                Id = id,
                Path = path ?? "",
                Position = position,
                Rotation = rotation,
                Scale = scale
            });
        }

        internal static int PendingCount => Pending.Count;

        internal static void ScheduleDoneWork()
        {
            if (_doneWorkStarted)
                return;
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return;
            if (CustomMapGen.IsLoadingExistingMap)
                return;
            if (!World.Procedural)
                return;

            var config = CustomMapGen.Instance?.GetConfig();
            if (config == null)
                return;

            bool needCompound = !config.SkipDeferredCompoundSpawnAtDone;
            if (!needCompound && Pending.Count == 0)
                return;

            _doneWorkStarted = true;
            float delay = config.DeferDoneWorkSeconds;
            if (delay < 0f)
                delay = 0f;

            // WorldSetup is destroyed right after DONE; ServerMgr is created later in Bootstrap.
            var host = new GameObject("CustomMapGen_DeferredSpawnRunner");
            UnityEngine.Object.DontDestroyOnLoad(host);
            var runner = host.AddComponent<DeferredSpawnRunner>();
            runner.Begin(DoneWorkCoroutine(delay, config, host));
        }

        private static IEnumerator DoneWorkCoroutine(float delaySeconds, MapGenConfig config, GameObject host)
        {
            // Wait until ServerMgr exists (map gen finished and Bootstrap created the server).
            float waited = 0f;
            while (ServerMgr.Instance == null && waited < 120f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (ServerMgr.Instance == null)
            {
                UnityEngine.Debug.LogWarning("[CustomMapGen] Deferred outpost spawn: ServerMgr never appeared; aborting.");
                if (host != null)
                    UnityEngine.Object.Destroy(host);
                yield break;
            }

            if (delaySeconds > 0f)
                yield return new WaitForSecondsRealtime(delaySeconds);

            if (!config.SkipDeferredCompoundSpawnAtDone)
                World_SpawnPrefabData_Deferred_Patch.RunDeferredCompoundSpawn();

            if (Pending.Count == 0)
            {
                if (host != null)
                    UnityEngine.Object.Destroy(host);
                yield break;
            }

            if (config.DebugLogging)
                UnityEngine.Debug.Log($"[CustomMapGen] [DEBUG] Deferred outpost spawn: starting for {Pending.Count} row(s) after map DONE/server ready.");

            yield return LoadPropsAssetScenes(config.DebugLogging);

            int spawned = 0;
            int failed = 0;
            MethodInfo spawnPrefab = AccessTools.Method(typeof(World), "SpawnPrefab",
                new[] { typeof(string), typeof(Prefab), typeof(Vector3), typeof(Quaternion), typeof(Vector3) });

            for (int i = 0; i < Pending.Count; i++)
            {
                DeferredRow row = Pending[i];
                Prefab prefab = Prefab.Load(row.Id);
                if (prefab?.Object == null)
                {
                    failed++;
                    if (config.DebugLogging)
                        UnityEngine.Debug.LogWarning($"[CustomMapGen] Deferred outpost spawn failed: id={row.Id} path={row.Path}");
                    continue;
                }

                try
                {
                    if (spawnPrefab != null)
                    {
                        spawnPrefab.Invoke(null, new object[] { row.Category, prefab, row.Position, row.Rotation, row.Scale });
                    }
                    else
                    {
                        GameObject go = prefab.Spawn(row.Position, row.Rotation, row.Scale);
                        if (go != null)
                            World.TrackSpawnedPrefab(row.Category, go);
                    }
                    spawned++;
                }
                catch (Exception ex)
                {
                    failed++;
                    UnityEngine.Debug.LogWarning($"[CustomMapGen] Deferred outpost spawn exception id={row.Id}: {ex.Message}");
                }
            }

            Pending.Clear();
            UnityEngine.Debug.Log($"[CustomMapGen] Deferred outpost spawn complete: spawned={spawned}, failed={failed} (after map finished).");

            if (host != null)
                UnityEngine.Object.Destroy(host);
        }

        private static IEnumerator LoadPropsAssetScenes(bool debug)
        {
            if (!(FileSystem.Backend is AssetBundleBackend backend))
                yield break;

            var scenes = new List<string>
            {
                "AssetScene-props.common",
                "AssetScene-props.other"
            };

            if (debug)
                UnityEngine.Debug.Log("[CustomMapGen] [DEBUG] Deferred outpost spawn: loading AssetScene-props.common/.other after map finished.");

            IEnumerator loading = backend.LoadAssetScenes(scenes);
            while (loading != null && loading.MoveNext())
                yield return loading.Current;
        }

        private sealed class DeferredSpawnRunner : MonoBehaviour
        {
            public void Begin(IEnumerator routine)
            {
                StartCoroutine(routine);
            }
        }
    }

    /// <summary>
    /// Schedule deferred outpost prop spawn when map generation reports DONE.
    /// Actual spawn runs later (server ready) so asset scenes load last, after the map is finished.
    /// </summary>
    [HarmonyPatch(typeof(UI_LoadingScreen), nameof(UI_LoadingScreen.Update), new Type[] { typeof(string) })]
    public static class UI_LoadingScreen_Update_DeferredOutpostSpawn_Patch
    {
        static void Prefix(string strType)
        {
            if (strType != "DONE")
                return;
            DeferredOutpostSpawn.ScheduleDoneWork();
        }
    }
}
