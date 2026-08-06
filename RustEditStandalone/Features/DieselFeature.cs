using System;
using System.Collections.Generic;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class DieselFeature
{
    public const string DieselPrefab = "assets/content/structures/excavator/prefabs/diesel_collectable.prefab";

    private static readonly List<Handler> Handlers = new();
    private static GameObject _runner;

    private sealed class Handler
    {
        public Vector3 Pos;
        public Quaternion Rot;
        public CollectibleEntity Entity;
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
        if (entity is not CollectibleEntity collectible) return;
        string prefab = entity.PrefabName ?? string.Empty;
        if (!prefab.Equals(DieselPrefab, StringComparison.OrdinalIgnoreCase) &&
            prefab.IndexOf("diesel_collectable", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        collectible.enableSaving = false;
        Handlers.Add(new Handler
        {
            Pos = collectible.transform.position,
            Rot = collectible.transform.rotation,
            Entity = collectible
        });
    }

    private static void OnKilled(BaseNetworkable networkable)
    {
        if (networkable is not CollectibleEntity collectible) return;
        Handler handler = null;
        for (int i = 0; i < Handlers.Count; i++)
            if (Handlers[i].Entity == collectible) { handler = Handlers[i]; break; }
        if (handler == null) return;
        handler.Entity = null;
        handler.Pending = true;
        float delay = RustEditConfig.Data.Respawn.Diesel.RandomSeconds;
        EnsureRunner();
        _runner.GetComponent<Runner>().Schedule(() => TryRespawn(handler), delay);
    }

    private static void TryRespawn(Handler handler)
    {
        if (handler == null || !handler.Pending) return;
        var hits = Physics.OverlapSphere(handler.Pos, 10f);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i]?.GetComponentInParent<BuildingBlock>() != null)
            {
                _runner.GetComponent<Runner>().Schedule(() => TryRespawn(handler), 60f);
                return;
            }
        }

        var spawned = MapDataHelper.InstantiatePrefab<CollectibleEntity>(DieselPrefab, handler.Pos, handler.Rot);
        if (spawned != null)
        {
            spawned.enableSaving = false;
            handler.Entity = spawned;
            handler.Pending = false;
        }
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        _runner = new GameObject("RustEditStandalone_Diesel");
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
