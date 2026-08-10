using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RoadFix.Bridge;

/// <summary>
/// Bridge cubes live in AssetScene-props.common, which is not loaded during Road/Rail Meshes.
/// Serialize during procgen, then live-spawn after the server is up (same idea as CustomMapGen outpost defer).
/// </summary>
internal static class DeferredBridgeSpawn
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
    private static bool _scheduled;

    public static int PendingCount => Pending.Count;

    public static void Clear()
    {
        Pending.Clear();
        _scheduled = false;
    }

    public static void Enqueue(string category, uint id, string path, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Pending.Add(new DeferredRow
        {
            Category = string.IsNullOrEmpty(category) ? "Decor" : category,
            Id = id,
            Path = path ?? "",
            Position = position,
            Rotation = rotation,
            Scale = scale
        });
    }

    public static void Schedule()
    {
        if (_scheduled || Pending.Count == 0 || World.Cached)
            return;

        _scheduled = true;
        var host = new GameObject("RoadFix_DeferredBridgeRunner");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<DeferredSpawnRunner>().Begin(SpawnCoroutine(host));

        if (RoadFixConfig.Config?.DebugLogging == true)
            Debug.Log($"[RoadFix] Scheduled deferred bridge spawn for {Pending.Count} prefab(s)");
    }

    private static IEnumerator SpawnCoroutine(GameObject host)
    {
        float waited = 0f;
        while (ServerMgr.Instance == null && waited < 120f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (ServerMgr.Instance == null)
        {
            Debug.LogWarning("[RoadFix] Deferred bridge spawn: ServerMgr never appeared; aborting.");
            if (host != null)
                UnityEngine.Object.Destroy(host);
            yield break;
        }

        // Brief settle so Bootstrap finishes asset warmup.
        yield return new WaitForSecondsRealtime(1f);

        yield return LoadPropsAssetScenes();

        MethodInfo spawnPrefab = AccessTools.Method(typeof(World), "SpawnPrefab",
            new[] { typeof(string), typeof(Prefab), typeof(Vector3), typeof(Quaternion), typeof(Vector3) });

        int spawned = 0;
        int failed = 0;
        for (int i = 0; i < Pending.Count; i++)
        {
            DeferredRow row = Pending[i];
            Prefab prefab = Prefab.Load(row.Id);
            if (prefab?.Object == null)
            {
                failed++;
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
                Debug.LogWarning($"[RoadFix] Deferred bridge spawn failed id={row.Id}: {ex.Message}");
            }
        }

        int total = Pending.Count;
        Pending.Clear();
        Debug.Log($"[RoadFix] Deferred bridge spawn complete: spawned={spawned}/{total} failed={failed}");

        if (host != null)
            UnityEngine.Object.Destroy(host);
    }

    private static IEnumerator LoadPropsAssetScenes()
    {
        if (FileSystem.Backend is not AssetBundleBackend backend)
            yield break;

        var scenes = new List<string>
        {
            AssetSceneManifest.PropsCommonSceneName,
            AssetSceneManifest.PropsOtherSceneName
        };

        if (RoadFixConfig.Config?.DebugLogging == true)
            Debug.Log("[RoadFix] Loading AssetScene-props.common/.other for bridge cubes...");

        IEnumerator loading = backend.LoadAssetScenes(scenes);
        while (loading != null && loading.MoveNext())
            yield return loading.Current;
    }

    private sealed class DeferredSpawnRunner : MonoBehaviour
    {
        public void Begin(IEnumerator routine) => StartCoroutine(routine);
    }
}
