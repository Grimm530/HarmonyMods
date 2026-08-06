using System;
using System.Collections.Generic;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class JunkPileFeature
{
    private static readonly List<Handler> Handlers = new();
    private static GameObject _runner;

    private sealed class Handler
    {
        public string Prefab;
        public Vector3 Pos;
        public Quaternion Rot;
        public JunkPile Entity;
        public bool Pending;
    }

    public static void Initialize()
    {
        RustEditHub.OnSpawned += OnSpawned;
        RustEditHub.OnKilled += OnKilled;
        EnsureRunner();
    }

    public static void Shutdown()
    {
        RustEditHub.OnSpawned -= OnSpawned;
        RustEditHub.OnKilled -= OnKilled;
        Handlers.Clear();
        if (_runner != null) { UnityEngine.Object.Destroy(_runner); _runner = null; }
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity is not JunkPile pile) return;
        if (entity.OwnerID != 0UL) return;
        pile.enableSaving = false;
        Handlers.Add(new Handler
        {
            Prefab = pile.PrefabName,
            Pos = pile.transform.position,
            Rot = pile.transform.rotation,
            Entity = pile
        });
    }

    private static void OnKilled(BaseNetworkable networkable)
    {
        if (networkable is not JunkPile pile) return;
        Handler handler = null;
        for (int i = 0; i < Handlers.Count; i++)
            if (Handlers[i].Entity == pile) { handler = Handlers[i]; break; }
        if (handler == null) return;
        handler.Entity = null;
        handler.Pending = true;
        float delay = RustEditConfig.Data.Respawn.JunkPile.RandomSeconds;
        EnsureRunner();
        _runner.GetComponent<Runner>().Schedule(() =>
        {
            var spawned = MapDataHelper.InstantiatePrefab<JunkPile>(handler.Prefab, handler.Pos, handler.Rot);
            if (spawned != null)
            {
                spawned.enableSaving = false;
                handler.Entity = spawned;
                handler.Pending = false;
            }
        }, delay);
    }

    public static int RespawnAll()
    {
        int n = 0;
        for (int i = 0; i < Handlers.Count; i++)
        {
            var h = Handlers[i];
            if (!h.Pending) continue;
            var spawned = MapDataHelper.InstantiatePrefab<JunkPile>(h.Prefab, h.Pos, h.Rot);
            if (spawned != null)
            {
                spawned.enableSaving = false;
                h.Entity = spawned;
                h.Pending = false;
                n++;
            }
        }
        return n;
    }

    public static string Info()
    {
        int alive = 0, pending = 0;
        for (int i = 0; i < Handlers.Count; i++)
        {
            if (Handlers[i].Pending) pending++;
            else if (Handlers[i].Entity != null) alive++;
        }
        return $"JunkPile handlers={Handlers.Count} alive={alive} pending={pending}";
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        _runner = new GameObject("RustEditStandalone_JunkPile");
        UnityEngine.Object.DontDestroyOnLoad(_runner);
        _runner.AddComponent<Runner>();
    }

    private sealed class Runner : MonoBehaviour
    {
        public void Schedule(Action a, float d) => StartCoroutine(Run(a, d));
        private System.Collections.IEnumerator Run(Action a, float d)
        {
            if (d > 0) yield return new WaitForSeconds(d);
            try { a?.Invoke(); } catch { }
        }
    }
}
