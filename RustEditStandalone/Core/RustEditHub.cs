using System;
using System.Collections;
using System.Collections.Generic;
using RustEditStandalone.Config;
using UnityEngine;

namespace RustEditStandalone.Core;

/// <summary>
/// Central event bus and deferred process queue (parity with Oxide RustEditCore).
/// </summary>
public static class RustEditHub
{
    public static event Action OnLoaded;
    public static event Action OnServerInit;
    public static event Action<BaseEntity, string> OnSpawned;
    public static event Action<BaseNetworkable> OnKilled;

    private static readonly Queue<IEnumerator> ProcessQueue = new();
    private static bool _loadedRaised;
    private static bool _serverInitRaised;
    private static bool _processing;
    private static GameObject _runner;

    public static bool IsMapDataProcessed => _serverInitRaised;
    public static bool IsLoadingGrace { get; private set; }

    public static void Reset()
    {
        OnLoaded = null;
        OnServerInit = null;
        OnSpawned = null;
        OnKilled = null;
        ProcessQueue.Clear();
        _loadedRaised = false;
        _serverInitRaised = false;
        _processing = false;
        IsLoadingGrace = false;
        if (_runner != null)
        {
            UnityEngine.Object.Destroy(_runner);
            _runner = null;
        }
    }

    public static void Enqueue(IEnumerator routine)
    {
        if (routine == null) return;
        ProcessQueue.Enqueue(routine);
    }

    public static void NotifyPrefabTracked(GameObject go, string category)
    {
        if (!_loadedRaised)
        {
            _loadedRaised = true;
            MapDataHelper.SyncPrefabCount();
            try { OnLoaded?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[RustEditStandalone] OnLoaded error: " + ex); }
        }

        if (go == null) return;
        var entity = go.GetComponent<BaseEntity>();
        if (entity == null) return;

        try { OnSpawned?.Invoke(entity, category ?? string.Empty); }
        catch (Exception ex) { Debug.LogWarning("[RustEditStandalone] OnSpawned error: " + ex); }
    }

    public static void NotifyEntitySpawned(BaseNetworkable networkable)
    {
        if (networkable is not BaseEntity entity) return;
        try { OnSpawned?.Invoke(entity, entity.PrefabName ?? string.Empty); }
        catch (Exception ex) { Debug.LogWarning("[RustEditStandalone] OnEntitySpawned error: " + ex); }
    }

    public static void NotifyEntityKilled(BaseNetworkable networkable)
    {
        if (networkable == null) return;
        try { OnKilled?.Invoke(networkable); }
        catch (Exception ex) { Debug.LogWarning("[RustEditStandalone] OnKilled error: " + ex); }
    }

    public static void NotifySaveLoaded()
    {
        IsLoadingGrace = true;
        EnsureRunner();
        var mb = _runner.GetComponent<HubRunner>();
        mb.CancelInvoke(nameof(HubRunner.EndLoadingGrace));
        mb.Invoke(nameof(HubRunner.EndLoadingGrace), 120f);
    }

    public static void NotifyServerReady()
    {
        if (_serverInitRaised) return;
        EnsureRunner();
        var mb = _runner.GetComponent<HubRunner>();
        mb.CancelInvoke(nameof(HubRunner.StartProcessQueue));
        mb.Invoke(nameof(HubRunner.StartProcessQueue), 2f);
    }

    internal static void EndLoadingGraceInternal() => IsLoadingGrace = false;

    internal static void BeginProcessQueue()
    {
        if (_processing || _serverInitRaised) return;
        _processing = true;
        EnsureRunner();
        _runner.GetComponent<HubRunner>().StartCoroutine(DrainQueue());
    }

    private static IEnumerator DrainQueue()
    {
        while (ProcessQueue.Count > 0)
        {
            IEnumerator routine = ProcessQueue.Dequeue();
            if (routine != null)
                yield return routine;
            yield return CoroutineEx.waitForEndOfFrame;
        }

        _serverInitRaised = true;
        _processing = false;

        try { OnServerInit?.Invoke(); }
        catch (Exception ex) { Debug.LogWarning("[RustEditStandalone] OnServerInit error: " + ex); }

        try { RustEditApi.RaiseMapDataProcessed(); }
        catch (Exception ex) { Debug.LogWarning("[RustEditStandalone] MapDataProcessed error: " + ex); }

        Debug.Log("[RustEditStandalone] Map data processing complete.");
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        _runner = new GameObject("RustEditStandalone_Hub");
        UnityEngine.Object.DontDestroyOnLoad(_runner);
        _runner.AddComponent<HubRunner>();
    }

    private sealed class HubRunner : MonoBehaviour
    {
        public void StartProcessQueue() => BeginProcessQueue();
        public void EndLoadingGrace() => EndLoadingGraceInternal();
    }
}
