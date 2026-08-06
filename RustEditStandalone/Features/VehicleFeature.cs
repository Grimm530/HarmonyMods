using System;
using System.Collections;
using System.Collections.Generic;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class VehicleFeature
{
    private static readonly string[] MapKeys = { "vehicles", "vehicle", "rustedit_vehicles", "rustedit_vehicle" };
    private static SerializedVehicleData _data;
    private static readonly List<Handler> Handlers = new();
    private static GameObject _runner;

    private sealed class Handler
    {
        public string Prefab;
        public Vector3 Pos;
        public Quaternion Rot;
        public BaseEntity Entity;
        public bool Pending;
    }

    public static void Initialize()
    {
        RustEditHub.OnLoaded += Load;
        RustEditHub.Enqueue(SetupVehicles());
        RustEditHub.OnKilled += OnKilled;
        EnsureRunner();
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        RustEditHub.OnKilled -= OnKilled;
        Handlers.Clear();
        _data = null;
        if (_runner != null) { UnityEngine.Object.Destroy(_runner); _runner = null; }
    }

    private static void Load()
    {
        _data = null;
        if (MapDataHelper.TryGetMapXml(MapKeys, out SerializedVehicleData data))
        {
            _data = data;
            Debug.Log($"[RustEditStandalone] Vehicle entries: {_data?.vehicles?.Count ?? 0}");
        }
    }

    private static IEnumerator SetupVehicles()
    {
        yield return new WaitForSeconds(2f);
        if (_data?.vehicles == null) yield break;

        for (int i = 0; i < _data.vehicles.Count; i++)
        {
            var v = _data.vehicles[i];
            if (v == null) continue;
            string prefab = !string.IsNullOrEmpty(v.category) ? v.category : StringPool.Get(v.id);
            if (string.IsNullOrEmpty(prefab)) continue;
            Vector3 pos = v.position != null ? v.position.ToVector3() : Vector3.zero;
            Quaternion rot = v.rotation != null ? Quaternion.Euler(v.rotation.ToVector3()) : Quaternion.identity;
            var ent = MapDataHelper.InstantiatePrefab<BaseEntity>(prefab, pos, rot);
            if (ent == null) continue;
            ent.enableSaving = false;
            Handlers.Add(new Handler { Prefab = prefab, Pos = pos, Rot = rot, Entity = ent });
        }
    }

    private static void OnKilled(BaseNetworkable networkable)
    {
        if (networkable is not BaseEntity entity) return;
        Handler handler = null;
        for (int i = 0; i < Handlers.Count; i++)
            if (Handlers[i].Entity == entity) { handler = Handlers[i]; break; }
        if (handler == null) return;
        handler.Entity = null;
        handler.Pending = true;
        float delay = RustEditConfig.Data.Respawn.Vehicles.RandomSeconds;
        EnsureRunner();
        _runner.GetComponent<Runner>().Schedule(() =>
        {
            var spawned = MapDataHelper.InstantiatePrefab<BaseEntity>(handler.Prefab, handler.Pos, handler.Rot);
            if (spawned != null)
            {
                spawned.enableSaving = false;
                handler.Entity = spawned;
                handler.Pending = false;
            }
        }, delay);
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        _runner = new GameObject("RustEditStandalone_Vehicle");
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
