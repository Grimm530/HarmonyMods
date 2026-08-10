using HarmonyLib;
using UnityEngine;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
public static class Patch_BasePlayer_Die
{
    static void Postfix(BasePlayer __instance, HitInfo info)
    {
        if (__instance == null) return;
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        var victimIsNpc = __instance.IsNpc || !SteamIdHelper.IsSteamId(__instance.userID);

        // Victim stats (deaths) — real players only
        if (!victimIsNpc && mod.TryGetStats(__instance.userID, out var victimStats))
        {
            victimStats.AddStats(LootType.Death, "deaths", 1f);
            if (info != null && info.Initiator != null)
                victimStats.AddStats(LootType.Death, info.Initiator.ShortPrefabName ?? "unknown", 1f);
        }

        // Killer stats
        var killer = info?.InitiatorPlayer;
        if (killer == null || killer.IsNpc || !SteamIdHelper.IsSteamId(killer.userID) || killer == __instance)
            return;

        // Always record scientist/NPC prefab keys for Discord "NPC kills" (UltimateLeaderboard parity).
        // BaseCombatEntity.Die skips BasePlayer, so this is the only path for scientists.
        if (victimIsNpc)
        {
            var npcPrefab = GetNpcKillKey(__instance);
            mod.RecordStat(killer.userID, LootType.Kill, npcPrefab, 1f);
            // Optional: also count NPC kills toward generic "kills" (Killers). Off by default for PvE.
            if (mod.GetConfig()?.CountNpcKillsAsPlayerKills == true)
                mod.RecordStat(killer.userID, LootType.Kill, "kills", 1f);

            float npcDist = info?.ProjectileDistance ?? 0f;
            if (npcDist > 0 && mod.TryGetStats(killer.userID, out var npcAtk) &&
                (!npcAtk.TryGetItem(LootType.Kill, "max_distance", out var npcOld) || npcDist > npcOld))
                mod.RecordStatSet(killer.userID, LootType.Kill, "max_distance", (float)System.Math.Round(npcDist, 2));
            return;
        }

        // Real player kill → Killers category (LootType.Kill / "kills")
        mod.RecordStat(killer.userID, LootType.Kill, "kills", 1f);
        if (__instance.IsSleeping())
            mod.RecordStat(killer.userID, LootType.Kill, "kill_sleepers", 1f);

        float dist = info?.ProjectileDistance ?? 0f;
        if (dist > 0 && mod.TryGetStats(killer.userID, out var attackerStats) &&
            (!attackerStats.TryGetItem(LootType.Kill, "max_distance", out var old) || dist > old))
            mod.RecordStatSet(killer.userID, LootType.Kill, "max_distance", (float)System.Math.Round(dist, 2));
    }

    /// <summary>Prefab key used by Discord/UltimateLeaderboard NPC kill categories.</summary>
    static string GetNpcKillKey(BasePlayer victim)
    {
        if (victim == null) return "scientist";
        // Custom NPC skins used by popular plugins (Oxide UltimateLeaderboard parity).
        if (victim.skinID == 14922524) return "raidbase_npc";
        var typeName = victim.GetType().Name;
        if (typeName == "ZombieNPC") return "horde_npc";
        var prefab = victim.ShortPrefabName;
        return string.IsNullOrEmpty(prefab) ? "scientist" : prefab;
    }
}
