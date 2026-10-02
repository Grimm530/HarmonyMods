using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
public static class Patch_BasePlayer_Die
{
    static void Postfix(BasePlayer __instance, HitInfo info)
    {
        var mod = LeaderboardMod.Instance;
        if (mod == null || __instance == null) return;

        // Scientists and other NPC players are not PvP kills. Their prefab is the stat key,
        // so newer types (outbreak, boat, RHIB, scientist2) are tracked without a config list.
        bool victimNpc = __instance.IsNpc || !SteamIdHelper.IsSteamId(__instance.userID);

        if (!victimNpc && mod.TryGetStats(__instance.userID, out var victimStats))
        {
            victimStats.AddStats(LootType.Death, "deaths", 1f);
            if (info?.Initiator != null)
                victimStats.AddStats(LootType.Death, info.Initiator.ShortPrefabName ?? "unknown", 1f);
        }

        var killer = info?.InitiatorPlayer;
        if (killer == null || killer.IsNpc || !SteamIdHelper.IsSteamId(killer.userID) || killer == __instance)
            return;
        if (!mod.TryGetStats(killer.userID, out var attackerStats))
            return;

        if (victimNpc)
        {
            attackerStats.AddStats(LootType.Kill, NpcKillName.From(__instance), 1f);
            return;
        }

        attackerStats.AddStats(LootType.Kill, "kills", 1f);
        if (__instance.IsSleeping())
            attackerStats.AddStats(LootType.Kill, "kill_sleepers", 1f);

        float dist = info?.ProjectileDistance ?? 0f;
        if (dist <= 0) return;
        if (attackerStats.TryGetItem(LootType.Kill, "max_distance", out var old) && dist <= old) return;
        attackerStats.SetStats(LootType.Kill, "max_distance", (float)System.Math.Round(dist, 2));
    }
}
