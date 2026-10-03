using System;
using System.Collections.Generic;
using HarmonyLib;
using Rust.Ai.Gen2;
using UnityEngine;

namespace LimitEntities;

/// <summary>
/// Penned livestock counts against the tool cupboard owner. Each prefab (cow, bull, calf, sheep, lamb)
/// is its own type. Animals with no player cupboard are not owned and are not counted.
/// </summary>
internal static class LivestockLimits
{
    public const int DefaultPerType = 10;

    private static readonly Dictionary<ulong, ulong> CountedOwner = new Dictionary<ulong, ulong>();
    private static readonly HashSet<uint> Prefabs = new HashSet<uint>();
    private static bool _ready;

    public static void Install(LimitEntitiesService service)
    {
        if (service?.Config?.Permissions == null)
            return;

        Prefabs.Clear();
        DiscoverPrefabs(service);
        ApplyDefaultLimits(service);
        _ready = false;
        CountExisting();
        _ready = true;
        Debug.Log($"[LimitEntities] Livestock limit {DefaultPerType} of each type ({Prefabs.Count} prefabs). Penned animals need a player tool cupboard.");
    }

    public static void Reset()
    {
        _ready = false;
        CountedOwner.Clear();
        Prefabs.Clear();
    }

    public static void OnKilled(LivestockAnimal animal)
    {
        if (animal?.net == null)
            return;
        Uncount(animal.net.ID.Value, animal.prefabID);
    }

    public static void OnHomeChanged(LivestockAnimal animal, bool killInfantIfOver)
    {
        if (!_ready || animal == null || animal.IsDestroyed || animal.net == null)
            return;

        ulong netId = animal.net.ID.Value;
        CountedOwner.TryGetValue(netId, out ulong previous);
        bool owned = TryGetOwner(animal, out ulong owner);

        if (previous != 0 && (!owned || previous != owner))
            Uncount(netId, animal.prefabID);

        if (!owned)
            return;

        if (CountedOwner.TryGetValue(netId, out ulong current) && current == owner)
            return;

        LimitEntitiesService service = LimitEntitiesMod.Service;
        if (service == null || !Prefabs.Contains(animal.prefabID))
            return;

        LimitEntitiesService.PlayerData data = service.GetPlayerData(owner);
        data.UpdatePerms();
        int limit = LimitFor(data, animal.prefabID);
        if (!data.HasImmunity && data.PlayerEntities.GetEntityCount(animal.prefabID) >= limit)
        {
            animal.HomeTc = default;
            string message = "Livestock limit reached (" + limit + " " + animal.ShortPrefabName + ").";
            BasePlayer player = BasePlayer.FindByID(owner);
            service.HandleNotification(player, message, true);
            if (player == null)
                Debug.Log("[LimitEntities] " + owner + " " + message);
            if (killInfantIfOver && animal.IsInfant() && !animal.IsDestroyed)
                animal.Kill();
            return;
        }

        data.AddEntity(animal.prefabID);
        CountedOwner[netId] = owner;
    }

    public static bool BlockAgeUp(LivestockAnimal animal)
    {
        if (!_ready || animal == null || animal.Species == null)
            return false;
        if (!TryGetOwner(animal, out ulong owner))
            return false;

        GameObjectRef adult = animal.Species.AdultFor(animal.IsMale);
        if (adult == null || !adult.isValid)
            return false;

        uint prefabId = StringPool.Get(adult.resourcePath);
        if (prefabId == 0 || !Prefabs.Contains(prefabId))
            return false;

        LimitEntitiesService service = LimitEntitiesMod.Service;
        if (service == null)
            return false;

        LimitEntitiesService.PlayerData data = service.GetPlayerData(owner);
        data.UpdatePerms();
        if (data.HasImmunity)
            return false;
        if (data.PlayerEntities.GetEntityCount(prefabId) < LimitFor(data, prefabId))
            return false;

        string message = "Livestock limit reached (" + LimitFor(data, prefabId) + " " + NameOf(service, prefabId) + "). This calf will stay young until there is room.";
        service.HandleNotification(BasePlayer.FindByID(owner), message, true);
        try
        {
            AccessTools.Method(typeof(LivestockAnimal), "StartAgeTimer")?.Invoke(animal, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LimitEntities] Could not restart livestock grow timer: " + ex.Message);
        }
        return true;
    }

    private static void CountExisting()
    {
        foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
        {
            if (networkable is not LivestockAnimal animal || animal.IsDestroyed || animal.net == null)
                continue;
            if (!Prefabs.Contains(animal.prefabID) || !TryGetOwner(animal, out ulong owner))
                continue;
            LimitEntitiesService service = LimitEntitiesMod.Service;
            if (service == null)
                return;
            service.GetPlayerData(owner).AddEntity(animal.prefabID);
            CountedOwner[animal.net.ID.Value] = owner;
        }
    }

    private static void Uncount(ulong netId, uint prefabId)
    {
        if (!CountedOwner.TryGetValue(netId, out ulong owner))
            return;
        CountedOwner.Remove(netId);
        LimitEntitiesMod.Service?.GetPlayerData(owner).RemoveEntity(prefabId);
    }

    private static int LimitFor(LimitEntitiesService.PlayerData data, uint prefabId)
    {
        LimitEntitiesService.PermissionEntry perms = data.Perms;
        if (perms?.LimitsGlobal?.LimitEntitiesCache != null
            && perms.LimitsGlobal.LimitEntitiesCache.TryGetValue(prefabId, out int configured))
            return configured;
        return DefaultPerType;
    }

    private static bool TryGetOwner(LivestockAnimal animal, out ulong ownerId)
    {
        ownerId = 0;
        if (animal == null || !animal.TryGetHomeCupboard(out BuildingPrivlidge cupboard) || cupboard == null)
            return false;
        if (cupboard.OwnerID.IsSteamId())
        {
            ownerId = cupboard.OwnerID;
            return true;
        }
        if (cupboard.authorizedPlayers == null)
            return false;
        foreach (ulong userId in cupboard.authorizedPlayers)
        {
            if (!userId.IsSteamId())
                continue;
            ownerId = userId;
            return true;
        }
        return false;
    }

    private static void DiscoverPrefabs(LimitEntitiesService service)
    {
        StringPool.Init();
        if (StringPool.toNumber == null || GameManager.server == null)
            return;

        foreach (KeyValuePair<string, uint> entry in StringPool.toNumber)
        {
            if (!LooksLikeLivestock(entry.Key))
                continue;
            GameObject prefab;
            try
            {
                prefab = GameManager.server.FindPrefab(entry.Key);
            }
            catch
            {
                continue;
            }
            if (prefab == null || prefab.GetComponent<LivestockAnimal>() == null)
                continue;
            Prefabs.Add(entry.Value);
        }
    }

    private static void ApplyDefaultLimits(LimitEntitiesService service)
    {
        foreach (LimitEntitiesService.PermissionEntry entry in service.Config.Permissions)
        {
            if (entry?.LimitsGlobal?.LimitEntitiesCache == null || entry.LimitsGlobal.LimitsEntities == null)
                continue;
            foreach (uint prefabId in Prefabs)
            {
                if (entry.LimitsGlobal.LimitEntitiesCache.ContainsKey(prefabId))
                    continue;
                entry.LimitsGlobal.LimitEntitiesCache[prefabId] = DefaultPerType;
                if (StringPool.TryGet(prefabId, out string path) && !entry.LimitsGlobal.LimitsEntities.ContainsKey(path))
                    entry.LimitsGlobal.LimitsEntities[path] = DefaultPerType;
            }
        }
    }

    private static bool LooksLikeLivestock(string path)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (path.IndexOf("corpse", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return path.IndexOf("/cow", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("/bull", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("/calf", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("/sheep", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("/lamb", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("livestock", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NameOf(LimitEntitiesService service, uint prefabId)
    {
        string name = service.GetShortName(prefabId);
        return string.IsNullOrEmpty(name) ? prefabId.ToString() : name;
    }
}
