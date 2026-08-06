using System;
using System.Collections.Generic;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class LootFeature
{
    private static readonly string[] MapKeys = { "loot", "rustedit_loot", "lootable", "rustedit_lootable" };
    private static SerializedLootableContainerData _data;
    private static readonly List<Handler> Handlers = new();
    private static GameObject _runner;

    private sealed class Handler
    {
        public string Prefab;
        public Vector3 Pos;
        public Quaternion Rot;
        public LootableContainerData Profile;
        public LootContainer Entity;
        public bool Pending;
        public float DueAt;
    }

    public static void Initialize()
    {
        if (!RustEditConfig.Data.Spawnables.Loot) return;
        RustEditHub.OnLoaded += Load;
        RustEditHub.OnSpawned += OnSpawned;
        RustEditHub.OnKilled += OnKilled;
        EnsureRunner();
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        RustEditHub.OnSpawned -= OnSpawned;
        RustEditHub.OnKilled -= OnKilled;
        Handlers.Clear();
        _data = null;
        if (_runner != null) { UnityEngine.Object.Destroy(_runner); _runner = null; }
    }

    public static void CollectEntities(List<BaseEntity> list)
    {
        for (int i = 0; i < Handlers.Count; i++)
            if (Handlers[i].Entity != null) list.Add(Handlers[i].Entity);
    }

    private static void Load()
    {
        _data = null;
        if (MapDataHelper.TryGetMapXml(MapKeys, out SerializedLootableContainerData data))
        {
            _data = data;
            Debug.Log($"[RustEditStandalone] Loot profiles: {_data?.entities?.Count ?? 0}");
        }
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity is not LootContainer loot) return;
        if (entity.OwnerID != 0UL) return;

        LootableContainerData profile = FindProfile(category) ?? FindProfile(loot.PrefabName);
        Populate(loot, profile);

        var handler = new Handler
        {
            Prefab = loot.PrefabName,
            Pos = loot.transform.position,
            Rot = loot.transform.rotation,
            Profile = profile,
            Entity = loot
        };
        Handlers.Add(handler);

        if (profile != null && profile.refreshRateMax > 0)
        {
            float refresh = UnityEngine.Random.Range(profile.refreshRateMin, profile.refreshRateMax + 1) * 60f;
            loot.Invoke(() => Populate(loot, profile), refresh);
        }
    }

    private static void OnKilled(BaseNetworkable networkable)
    {
        if (networkable is not LootContainer loot) return;
        Handler handler = null;
        for (int i = 0; i < Handlers.Count; i++)
        {
            if (Handlers[i].Entity == loot) { handler = Handlers[i]; break; }
        }
        if (handler == null) return;

        handler.Entity = null;
        handler.Pending = true;
        float delay = handler.Profile != null
            ? UnityEngine.Random.Range(handler.Profile.respawnRateMin, handler.Profile.respawnRateMax + 1) * 60f
            : RustEditConfig.Data.Respawn.Loot.RandomSeconds;
        handler.DueAt = Time.realtimeSinceStartup + delay;
        EnsureRunner();
        _runner.GetComponent<Runner>().Schedule(() => TryRespawn(handler), delay);
    }

    private static void TryRespawn(Handler handler)
    {
        if (handler == null || !handler.Pending) return;
        if (PlayersNearby(handler.Pos, 20f) || BuildingsNearby(handler.Pos, 10f))
        {
            float retry = 60f;
            handler.DueAt = Time.realtimeSinceStartup + retry;
            _runner.GetComponent<Runner>().Schedule(() => TryRespawn(handler), retry);
            return;
        }

        var spawned = MapDataHelper.InstantiatePrefab<LootContainer>(handler.Prefab, handler.Pos, handler.Rot);
        if (spawned == null) return;
        handler.Entity = spawned;
        handler.Pending = false;
        Populate(spawned, handler.Profile);
    }

    public static int RespawnAll()
    {
        int n = 0;
        for (int i = 0; i < Handlers.Count; i++)
        {
            var h = Handlers[i];
            if (!h.Pending) continue;
            TryRespawn(h);
            if (!h.Pending) n++;
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
        return $"Loot handlers={Handlers.Count} alive={alive} pending={pending}";
    }

    private static LootableContainerData FindProfile(string category)
    {
        if (_data?.entities == null || string.IsNullOrEmpty(category)) return null;
        string filename = MapDataHelper.GetFilenameFromCategory(category);
        for (int i = 0; i < _data.entities.Count; i++)
        {
            var e = _data.entities[i];
            if (e?.filename != null && e.filename.Equals(filename, StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }

    private static void Populate(LootContainer loot, LootableContainerData profile)
    {
        if (loot?.inventory == null) return;
        loot.enableSaving = false;
        if (profile?.items == null || profile.items.Count == 0)
        {
            loot.SpawnLoot();
            return;
        }

        loot.inventory.Clear();
        int amount = UnityEngine.Random.Range(Mathf.Max(1, profile.spawnAmountMin), Mathf.Max(1, profile.spawnAmountMax) + 1);
        var pool = new List<LootableItemData>(profile.items);
        for (int i = 0; i < amount && pool.Count > 0; i++)
        {
            int idx = UnityEngine.Random.Range(0, pool.Count);
            var itemData = pool[idx];
            pool.RemoveAt(idx);
            int qty = UnityEngine.Random.Range(Mathf.Max(1, itemData.minimum), Mathf.Max(1, itemData.maximum) + 1);
            Item item;
            if (itemData.blueprint)
            {
                item = ItemManager.CreateByName("blueprintbase", 1, 0uL);
                var def = ItemManager.FindItemDefinition(itemData.shortname);
                if (item != null && def != null)
                    item.blueprintTarget = def.itemid;
            }
            else
            {
                item = ItemManager.CreateByName(itemData.shortname, qty, 0uL);
            }
            if (item != null)
                loot.inventory.Insert(item);
        }
    }

    private static bool PlayersNearby(Vector3 pos, float radius)
    {
        var players = BasePlayer.activePlayerList;
        float r2 = radius * radius;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p.IsNpc || p.IsDestroyed) continue;
            if ((p.transform.position - pos).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    private static bool BuildingsNearby(Vector3 pos, float radius)
    {
        var hits = Physics.OverlapSphere(pos, radius);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i]?.GetComponentInParent<BuildingBlock>() != null)
                return true;
        }
        return false;
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        _runner = new GameObject("RustEditStandalone_Loot");
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
