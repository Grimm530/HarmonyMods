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

        // Victim stats (deaths)
        if (!__instance.IsNpc && SteamIdHelper.IsSteamId(__instance.userID) && mod.TryGetStats(__instance.userID, out var victimStats))
        {
            victimStats.AddStats(LootType.Death, "deaths", 1f);
            if (info != null && info.Initiator != null)
                victimStats.AddStats(LootType.Death, info.Initiator.ShortPrefabName ?? "unknown", 1f);
        }

        // Killer stats
        var killer = info?.InitiatorPlayer;
        if (killer != null && !killer.IsNpc && SteamIdHelper.IsSteamId(killer.userID) && killer != __instance &&
            mod.TryGetStats(killer.userID, out var attackerStats))
        {
            attackerStats.AddStats(LootType.Kill, "kills", 1f);
            if (__instance.IsSleeping())
                attackerStats.AddStats(LootType.Kill, "kill_sleepers", 1f);

            float dist = info?.ProjectileDistance ?? 0f;
            if (dist > 0 && attackerStats.TryGetItem(LootType.Kill, "max_distance", out var old) && dist <= old) return;
            if (dist > 0)
                attackerStats.SetStats(LootType.Kill, "max_distance", (float)System.Math.Round(dist, 2));
        }
    }
}
