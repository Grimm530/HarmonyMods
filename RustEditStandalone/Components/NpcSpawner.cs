using System.Collections.Generic;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;
using UnityEngine.AI;

namespace RustEditStandalone.Components;

public sealed class NpcSpawner : MonoBehaviour
{
    private static readonly Dictionary<NpcType, string> Prefabs = new()
    {
        [NpcType.Scientist] = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
        [NpcType.Peacekeeper] = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_peacekeeper.prefab",
        [NpcType.HeavyScientist] = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab",
        [NpcType.JunkpileScientist] = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_junkpile.prefab",
        [NpcType.Bandit] = "assets/prefabs/npc/bandit/guard/bandit_guard.prefab",
        [NpcType.Murderer] = "assets/prefabs/npc/murderer/murderer.prefab",
        [NpcType.Scarecrow] = "assets/prefabs/npc/scarecrow/scarecrow.prefab"
    };

    private SerializedNpcSpawner _data;
    private string _prefabPath;
    private float _nextRespawnAt;
    private bool _respawnPending;

    public BaseCombatEntity ActiveNpc { get; private set; }

    public void Initialize(SerializedNpcSpawner data)
    {
        _data = data;
        var type = (NpcType)data.npcType;
        if (!Prefabs.TryGetValue(type, out _prefabPath))
            _prefabPath = Prefabs[NpcType.Scientist];

        if (data.position != null)
            transform.position = data.position.ToVector3();

        Invoke(nameof(DoRespawn), 3f);
        InvokeRepeating(nameof(CheckIfRespawnNeeded), 5f, 5f);
    }

    public void CheckIfRespawnNeeded()
    {
        if (ActiveNpc != null && !ActiveNpc.IsDestroyed) return;
        ActiveNpc = null;
        if (_respawnPending) return;
        ScheduleRespawn();
    }

    private void ScheduleRespawn()
    {
        _respawnPending = true;
        int min = _data?.respawnMin ?? 30;
        int max = _data?.respawnMax ?? 60;
        if (max < min) { int t = min; min = max; max = t; }
        float delay = UnityEngine.Random.Range(min, max + 1);
        _nextRespawnAt = Time.realtimeSinceStartup + delay;
        Invoke(nameof(DoRespawn), delay);
    }

    public void DoRespawn()
    {
        _respawnPending = false;
        if (ActiveNpc != null && !ActiveNpc.IsDestroyed) return;

        Vector3 pos = transform.position;
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 20f, -1))
            pos = hit.position;

        var entity = MapDataHelper.InstantiatePrefab<BaseCombatEntity>(_prefabPath, pos, Quaternion.identity);
        if (entity == null) return;

        entity.enableSaving = false;
        ActiveNpc = entity;

        if (entity is BasePlayer player)
            RustEditApi.RaiseNpcSpawned(player);
    }
}
