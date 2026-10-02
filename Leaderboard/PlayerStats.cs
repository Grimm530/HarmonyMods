using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Leaderboard;

public class PlayerStats
{
    public ulong UserId { get; set; }
    public string LastIP { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime ConnectTime { get; set; } = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateTime DisconnectTime { get; set; } = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public double TotalPlayTime { get; set; }
    public float Points { get; set; }
    public bool HiddenFromLeaderboard { get; set; }

    [JsonProperty(PropertyName = "StatsStorage", ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<LootType, Dictionary<string, float>> StatsStorage { get; set; }
        = new Dictionary<LootType, Dictionary<string, float>>();

    [JsonIgnore] public bool IsOnline { get; set; }

    public PlayerStats() { }

    public PlayerStats(ulong userId)
    {
        UserId = userId;
        ConnectTime = DateTime.UtcNow;
        TotalPlayTime = 0.0;
        StatsStorage = new Dictionary<LootType, Dictionary<string, float>>();
    }

    public void AddStats(LootType type, string prefab, float value)
    {
        if (string.IsNullOrEmpty(prefab)) return;
        if (!StatsStorage.TryGetValue(type, out var storage))
            StatsStorage[type] = storage = new Dictionary<string, float>();
        if (storage.TryGetValue(prefab, out var val))
            storage[prefab] = val + value;
        else
            storage[prefab] = value;
    }

    public void SetStats(LootType type, string prefab, float value)
    {
        if (string.IsNullOrEmpty(prefab)) return;
        if (!StatsStorage.TryGetValue(type, out var storage))
            StatsStorage[type] = storage = new Dictionary<string, float>();
        storage[prefab] = value;
    }

    public bool TryGetItem(LootType type, string key, out float value)
    {
        value = 0f;
        return !string.IsNullOrEmpty(key) &&
               StatsStorage.TryGetValue(type, out var storage) &&
               storage.TryGetValue(key, out value);
    }

    public float GetTotal(LootType type)
    {
        if (!StatsStorage.TryGetValue(type, out var storage)) return 0f;
        float sum = 0f;
        foreach (var v in storage.Values) sum += v;
        return sum;
    }

    public int GetKills() => TryGetItem(LootType.Kill, "kills", out var v) ? (int)v : 0;
    public int GetDeaths() => TryGetItem(LootType.Death, "deaths", out var v) ? (int)v : 0;

    /// <summary>Total of all animal kills (LootType.Kill except pvp and NPC keys).</summary>
    public int GetAnimalKills()
    {
        if (!StatsStorage.TryGetValue(LootType.Kill, out var storage)) return 0;
        int sum = 0;
        foreach (var kv in storage)
        {
            if (NpcKillName.IsPvpKey(kv.Key) || NpcKillName.IsNpcKey(kv.Key)) continue;
            sum += (int)kv.Value;
        }
        return sum;
    }

    /// <summary>Total NPC kills (helicopter, bradley, scientists, and the newer scientist variants).</summary>
    public int GetNpcKills()
    {
        if (!StatsStorage.TryGetValue(LootType.Kill, out var storage)) return 0;
        int sum = 0;
        foreach (var kv in storage)
        {
            if (NpcKillName.IsNpcKey(kv.Key)) sum += (int)kv.Value;
        }
        return sum;
    }

    public Dictionary<string, float> GetAll(LootType type) =>
        StatsStorage.TryGetValue(type, out var s) ? s : new Dictionary<string, float>();

    /// <summary>Sum Construction stats where key starts with or contains the given term (case-insensitive).</summary>
    public int GetConstructionByKey(string keyStartsWithOrContains)
    {
        if (!StatsStorage.TryGetValue(LootType.Construction, out var storage)) return 0;
        var term = keyStartsWithOrContains.ToLowerInvariant();
        int sum = 0;
        foreach (var kv in storage)
        {
            var k = kv.Key?.ToLowerInvariant() ?? "";
            if (k.StartsWith(term, StringComparison.OrdinalIgnoreCase) || k.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                sum += (int)kv.Value;
        }
        return sum;
    }

    public int GetFoundations() => GetConstructionByKey("foundation");
    public int GetWalls() => GetConstructionByKey("wall");
    public int GetFloors() => GetConstructionByKey("floor");
    public int GetDoors() => GetConstructionByKey("door");
    public int GetToolCupboards() => GetConstructionByKey("cupboard");

    /// <summary>Sum stats for a list of (LootType, key) entries (for section totals e.g. RESOURCES, FARMING, MISC).</summary>
    public float GetSumForEntries(IReadOnlyList<(LootType type, string key)> entries)
    {
        if (entries == null) return 0f;
        float sum = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (TryGetItem(e.type, e.key, out var v)) sum += v;
        }
        return sum;
    }

    /// <summary>Current session only. A missing or epoch connect time contributes nothing, so playtime cannot jump to years.</summary>
    public double GetCurrentPlayTimeSeconds()
    {
        if (!IsOnline || ConnectTime.Year < 2000) return 0;
        var session = (DateTime.UtcNow - ConnectTime).TotalSeconds;
        return session > 0 ? session : 0;
    }

    public double GetTotalPlayTimeIncludingCurrent() =>
        TotalPlayTime + GetCurrentPlayTimeSeconds();

    /// <summary>
    /// Fold the open session into TotalPlayTime once. A second disconnect or unload does not add it again.
    /// Connect times before year 2000 are ignored so a bad date cannot inflate playtime.
    /// </summary>
    public void CommitOnlineSession()
    {
        if (!IsOnline) return;
        var connect = ConnectTime;
        var now = DateTime.UtcNow;
        IsOnline = false;
        ConnectTime = now;
        DisconnectTime = now;
        if (connect.Year < 2000) return;
        var session = (now - connect).TotalSeconds;
        if (session > 0)
            TotalPlayTime += session;
    }

    /// <summary>PvP hit count for a body part (head, chest, stomach, arm, leg).</summary>
    public float GetBodyHits(string areaKey)
    {
        return TryGetItem(LootType.BodyHits, areaKey ?? "", out var v) ? v : 0f;
    }

    /// <summary>Total PvP hits across all body parts (for hitrate %).</summary>
    public float GetTotalBodyHits()
    {
        return GetTotal(LootType.BodyHits);
    }
}

/// <summary>Kill-stat keys for players, animals, and NPC prefabs including the 1.5.55 scientist variants.</summary>
public static class NpcKillName
{
    public static string From(BaseEntity entity)
    {
        if (entity == null) return "scientist";
        if (entity.skinID == 14922524UL || entity.skinID == GrimmCoreBridge.LegacyRaidableBasesSkinId)
            return "raidbase_npc";
        if (string.Equals(entity.GetType().Name, "ZombieNPC", StringComparison.Ordinal))
            return "horde_npc";
        if (entity.skinID == GrimmCoreBridge.LegacyGrimmNpcSkinId && entity is ScientistNPC npc &&
            !string.IsNullOrEmpty(npc.displayName))
        {
            var name = npc.displayName;
            return name.Length > 64 ? name.Substring(0, 64) : name;
        }

        var prefab = entity.ShortPrefabName;
        return string.IsNullOrEmpty(prefab) ? "scientist" : prefab;
    }

    public static bool IsPvpKey(string key) =>
        key == "kills" || key == "kill_sleepers" || key == "max_distance";

    public static bool IsNpcKey(string key)
    {
        if (string.IsNullOrEmpty(key) || IsPvpKey(key)) return false;
        if (key == "helicopter" || key == "bradleyapc" || key == "scientist" || key == "raidbase_npc" || key == "horde_npc")
            return true;
        if (key.StartsWith("scientist", StringComparison.OrdinalIgnoreCase)) return true;
        if (key.StartsWith("npc_", StringComparison.OrdinalIgnoreCase)) return true;
        if (key.StartsWith("zombie", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
