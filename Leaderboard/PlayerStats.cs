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
        var exclude = new HashSet<string> { "kills", "kill_sleepers", "max_distance", "helicopter", "bradleyapc", "raidbase_npc", "horde_npc" };
        int sum = 0;
        foreach (var kv in storage)
        {
            var key = kv.Key ?? "";
            if (exclude.Contains(key)) continue;
            if (key.EndsWith(".corpse", StringComparison.OrdinalIgnoreCase)) continue;
            if (key.IndexOf("scientist", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (key.IndexOf("npc", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            // Deployables / barrels are not animals
            if (key.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (key.IndexOf("deployed", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (key.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            sum += (int)kv.Value;
        }
        return sum;
    }

    /// <summary>Total NPC kills (scientists/dwellers by prefab + heli/Bradley).</summary>
    public int GetNpcKills()
    {
        if (!StatsStorage.TryGetValue(LootType.Kill, out var storage)) return 0;
        var exclude = new HashSet<string> { "kills", "kill_sleepers", "max_distance" };
        int sum = 0;
        foreach (var kv in storage)
        {
            var key = kv.Key ?? "";
            if (exclude.Contains(key)) continue;
            // Animals are counted separately; skip common animal prefabs for the NPC total.
            if (key is "bear" or "polarbear" or "boar" or "chicken" or "stag" or "wolf2" or "panther"
                or "crocodile" or "snake.entity" or "tiger" or "simpleshark")
                continue;
            if (key.EndsWith(".corpse", System.StringComparison.OrdinalIgnoreCase)) continue;
            // Scientists, dwellers, heli, bradley, custom NPC keys, etc.
            if (key.IndexOf("scientist", System.StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("npc", System.StringComparison.OrdinalIgnoreCase) >= 0
                || key is "helicopter" or "bradleyapc" or "raidbase_npc" or "horde_npc")
                sum += (int)kv.Value;
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

    /// <summary>Current session play time + total (for display).</summary>
    public double GetCurrentPlayTimeSeconds() =>
        IsOnline ? (DateTime.UtcNow - ConnectTime).TotalSeconds : 0;

    public double GetTotalPlayTimeIncludingCurrent() =>
        TotalPlayTime + GetCurrentPlayTimeSeconds();

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
