using System;
using System.Collections.Generic;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class DeployableFeature
{
    private static readonly HashSet<string> StripNames = new(StringComparer.Ordinal)
    {
        "GroundWatch", "DestroyOnGroundMissing"
    };

    private static readonly List<BaseCombatEntity> MapEntities = new();
    private static readonly HashSet<ulong> MapKeys = new();
    private static readonly Dictionary<NetworkableId, PendingRespawn> Pending = new();

    private struct PendingRespawn
    {
        public string Prefab;
        public Vector3 Pos;
        public Quaternion Rot;
        public float DueAt;
    }

    private static GameObject _runner;

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
        MapEntities.Clear();
        MapKeys.Clear();
        Pending.Clear();
        if (_runner != null) { UnityEngine.Object.Destroy(_runner); _runner = null; }
    }

    public static void CollectEntities(List<BaseEntity> list)
    {
        for (int i = 0; i < MapEntities.Count; i++)
            if (MapEntities[i] != null) list.Add(MapEntities[i]);
    }

    public static bool IsMapEntity(BaseNetworkable entity)
    {
        if (entity == null || entity.net == null) return false;
        if (IoFeature.IsMapIo(entity)) return true;
        for (int i = 0; i < MapEntities.Count; i++)
        {
            var e = MapEntities[i];
            if (e != null && e.net != null && e.net.ID == entity.net.ID)
                return true;
        }
        return false;
    }

    public static bool ShouldBlockDamage(BaseCombatEntity entity)
    {
        if (entity == null) return false;
        if (RustEditHub.IsLoadingGrace) return false;
        if (!IsMapEntity(entity) && !IoFeature.IsMapIo(entity)) return false;
        // Allow interaction damage on some interactive types is handled by callers; default block.
        return true;
    }

    public static bool ShouldBlockStability(StabilityEntity entity) => IsMapEntity(entity);

    public static void OnTrapTriggered(BaseTrap trap)
    {
        if (trap == null || !IsMapEntity(trap)) return;
        float delay = RustEditConfig.Data.Respawn.Traps.RandomSeconds;
        EnsureRunner();
        _runner.GetComponent<Runner>().Schedule(() =>
        {
            if (trap != null && !trap.IsDestroyed)
                trap.Arm();
        }, delay);
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity is not BaseCombatEntity combat) return;
        if (entity is BasePlayer) return;
        if (entity is LootContainer) return;
        if (entity is ResourceEntity) return;
        if (entity is JunkPile) return;
        if (entity is BradleyAPC) return;
        if (entity is BaseVehicle) return;

        // Only track map-category / IO / deployables without owner
        bool mapOwned = entity.OwnerID == 0UL || (!string.IsNullOrEmpty(category) && category.IndexOf("rustedit", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!mapOwned && entity is not IOEntity) return;

        Harden(combat);
        Track(combat);
    }

    private static void OnKilled(BaseNetworkable networkable)
    {
        if (networkable is not BaseCombatEntity combat) return;
        if (!IsMapEntity(combat)) return;

        // Trap / barricade respawn
        if (combat is BaseTrap || combat is Barricade || combat is SamSite)
        {
            var pending = new PendingRespawn
            {
                Prefab = combat.PrefabName,
                Pos = combat.transform.position,
                Rot = combat.transform.rotation,
                DueAt = Time.realtimeSinceStartup + RustEditConfig.Data.Respawn.Traps.RandomSeconds
            };
            EnsureRunner();
            _runner.GetComponent<Runner>().Schedule(() => Respawn(pending), pending.DueAt - Time.realtimeSinceStartup);
        }

        Untrack(combat);
    }

    private static void Respawn(PendingRespawn pending)
    {
        if (string.IsNullOrEmpty(pending.Prefab)) return;
        // Skip if something already occupies the spot
        var hits = Physics.OverlapSphere(pending.Pos, 1f);
        for (int i = 0; i < hits.Length; i++)
        {
            var e = hits[i]?.GetComponentInParent<BaseEntity>();
            if (e != null && !e.IsDestroyed && e.PrefabName == pending.Prefab)
                return;
        }

        var spawned = MapDataHelper.InstantiatePrefab<BaseCombatEntity>(pending.Prefab, pending.Pos, pending.Rot);
        if (spawned != null)
        {
            Harden(spawned);
            Track(spawned);
        }
    }

    private static void Harden(BaseCombatEntity entity)
    {
        if (entity == null) return;
        entity.enableSaving = false;
        var comps = entity.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c != null && StripNames.Contains(c.GetType().Name))
                UnityEngine.Object.Destroy(c);
        }
        if (entity is DecayEntity decay)
        {
            decay.CancelInvoke(nameof(DecayEntity.DecayTick));
            decay.decay = null;
        }
        if (entity is StabilityEntity stability)
            stability.grounded = true;
    }

    private static void Track(BaseCombatEntity entity)
    {
        if (entity?.net == null) return;
        ulong key = entity.prefabID ^ (ulong)(entity.transform.position.x * 100) ^ (ulong)(entity.transform.position.z * 100);
        MapKeys.Add(key);
        if (!MapEntities.Contains(entity))
            MapEntities.Add(entity);
    }

    private static void Untrack(BaseCombatEntity entity)
    {
        MapEntities.Remove(entity);
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;
        _runner = new GameObject("RustEditStandalone_Deployable");
        UnityEngine.Object.DontDestroyOnLoad(_runner);
        _runner.AddComponent<Runner>();
    }

    private sealed class Runner : MonoBehaviour
    {
        public void Schedule(Action action, float delay)
        {
            StartCoroutine(Run(action, delay));
        }

        private System.Collections.IEnumerator Run(Action action, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            try { action?.Invoke(); } catch (Exception ex) { Debug.LogWarning("[RustEditStandalone] Deployable schedule: " + ex.Message); }
        }
    }
}
