using System;
using System.Collections.Generic;
using RustEditStandalone.Core;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class SpawnFeature
{
    public const string SpawnPointPrefab = "assets/bundled/prefabs/modding/volumes_and_triggers/spawn_point.prefab";

    public static readonly List<Transform> SpawnPoints = new();
    private static readonly List<Transform> Pool = new();
    public static bool HasCustomSpawns => SpawnPoints.Count > 0;

    public static void Initialize()
    {
        RustEditHub.OnSpawned += OnSpawned;
    }

    public static void Shutdown()
    {
        RustEditHub.OnSpawned -= OnSpawned;
        SpawnPoints.Clear();
        Pool.Clear();
    }

    private static void OnSpawned(BaseEntity entity, string category)
    {
        if (entity == null) return;
        string prefab = entity.PrefabName ?? string.Empty;
        if (!prefab.Equals(SpawnPointPrefab, StringComparison.OrdinalIgnoreCase) &&
            prefab.IndexOf("spawn_point", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (!SpawnPoints.Contains(entity.transform))
            SpawnPoints.Add(entity.transform);
    }

    public static bool TryGetSpawnPoint(out BasePlayer.SpawnPoint spawnPoint)
    {
        spawnPoint = null;
        if (SpawnPoints.Count == 0) return false;

        if (Pool.Count == 0)
        {
            for (int i = 0; i < SpawnPoints.Count; i++)
                if (SpawnPoints[i] != null) Pool.Add(SpawnPoints[i]);
            Shuffle(Pool);
        }

        while (Pool.Count > 0)
        {
            int last = Pool.Count - 1;
            var t = Pool[last];
            Pool.RemoveAt(last);
            if (t == null) continue;
            spawnPoint = new BasePlayer.SpawnPoint
            {
                pos = t.position,
                rot = t.rotation
            };
            return true;
        }
        return false;
    }

    public static void Show(BasePlayer player, float seconds)
    {
        if (player == null) return;
        float dur = seconds <= 0 ? 30f : seconds;
        for (int i = 0; i < SpawnPoints.Count; i++)
        {
            var t = SpawnPoints[i];
            if (t == null) continue;
            player.SendConsoleCommand("ddraw.sphere", dur, Color.magenta, t.position, 1f);
            player.SendConsoleCommand("ddraw.text", dur, Color.magenta, t.position + Vector3.up, $"spawn {i}");
        }
    }

    private static void Shuffle(List<Transform> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
