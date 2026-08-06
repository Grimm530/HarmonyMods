using System;
using System.Collections.Generic;
using RustEditStandalone.Components;
using RustEditStandalone.Features;
using UnityEngine;

namespace RustEditStandalone.Core;

/// <summary>
/// Public API for other mods/plugins. Exposed via AppDomain.SetData("RustEdit_ApiType", typeof(RustEditApi)).
/// </summary>
public static class RustEditApi
{
    public static event Action<BasePlayer> NPCSpawned;
    public static event Action<BradleyAPC> APCSpawned;
    public static event Action MapDataProcessed;

    public static string[] GetTopologyMapNames()
    {
        return CustomTopologyFeature.GetNames();
    }

    public static bool TryGetTopologyMap(string name, out TerrainTopologyMap terrainTopologyMap)
    {
        return CustomTopologyFeature.TryGet(name, out terrainTopologyMap);
    }

    public static void GetAllMapEntities(ref List<BaseEntity> list)
    {
        if (list == null) return;
        DeployableFeature.CollectEntities(list);
        IoFeature.CollectEntities(list);
        LootFeature.CollectEntities(list);
        ResourceFeature.CollectEntities(list);
        VendingFeature.CollectEntities(list);
    }

    public static void GetMapEntitiesOfType<T>(ref List<T> list) where T : BaseEntity
    {
        if (list == null) return;
        var temp = new List<BaseEntity>();
        GetAllMapEntities(ref temp);
        for (int i = 0; i < temp.Count; i++)
        {
            if (temp[i] is T typed)
                list.Add(typed);
        }
    }

    public static void GetActiveNPCs(ref List<BaseCombatEntity> list)
    {
        if (list == null) return;
        var spawners = NpcFeature.Spawners;
        for (int i = 0; i < spawners.Count; i++)
        {
            var npc = spawners[i]?.ActiveNpc;
            if (npc != null)
                list.Add(npc);
        }
    }

    public static void GetActiveAPCs(ref List<BradleyAPC> list)
    {
        if (list == null) return;
        var spawners = ApcFeature.Spawners;
        for (int i = 0; i < spawners.Count; i++)
        {
            var apc = spawners[i]?.ActiveApc;
            if (apc != null)
                list.Add(apc);
        }
    }

    public static void GetSpawnpoints(ref List<Transform> list)
    {
        if (list == null) return;
        var points = SpawnFeature.SpawnPoints;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] != null)
                list.Add(points[i]);
        }
    }

    internal static void RaiseNpcSpawned(BasePlayer npc) => NPCSpawned?.Invoke(npc);
    internal static void RaiseAPCSpawned(BradleyAPC apc) => APCSpawned?.Invoke(apc);
    internal static void RaiseMapDataProcessed() => MapDataProcessed?.Invoke();
}
