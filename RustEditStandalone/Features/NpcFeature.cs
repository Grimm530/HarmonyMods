using System.Collections;
using System.Collections.Generic;
using RustEditStandalone.Components;
using RustEditStandalone.Config;
using RustEditStandalone.Core;
using RustEditStandalone.Data;
using UnityEngine;

namespace RustEditStandalone.Features;

public static class NpcFeature
{
    private static readonly string[] MapKeys = { "npc", "npcs", "rustedit_npc", "rustedit_npcs" };
    public static readonly List<NpcSpawner> Spawners = new();
    private static SerializedNpcData _data;

    public static void Initialize()
    {
        if (!RustEditConfig.Data.Spawnables.NPCs) return;
        RustEditHub.OnLoaded += Load;
        RustEditHub.Enqueue(CreateSpawners());
    }

    public static void Shutdown()
    {
        RustEditHub.OnLoaded -= Load;
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i] != null) Object.Destroy(Spawners[i].gameObject);
        Spawners.Clear();
        _data = null;
    }

    private static void Load()
    {
        _data = null;
        if (MapDataHelper.TryGetMapXml(MapKeys, out SerializedNpcData data))
        {
            _data = data;
            Debug.Log($"[RustEditStandalone] NPC spawners: {_data?.npcSpawners?.Count ?? 0}");
        }
    }

    private static IEnumerator CreateSpawners()
    {
        yield return new WaitForSeconds(1f);
        if (_data?.npcSpawners == null) yield break;
        for (int i = 0; i < _data.npcSpawners.Count; i++)
        {
            var entry = _data.npcSpawners[i];
            if (entry == null) continue;
            var go = new GameObject("RustEdit_NPC_" + i);
            Object.DontDestroyOnLoad(go);
            var spawner = go.AddComponent<NpcSpawner>();
            spawner.Initialize(entry);
            Spawners.Add(spawner);
        }
    }
}
