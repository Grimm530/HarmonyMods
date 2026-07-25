using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// Record body-part hits for hitrate charts when a real player damages a BasePlayer target.
/// Victims may be human players (always) or NPC BasePlayers (scientists, etc.) when
/// CountNpcHitsForHitrate is enabled — Oxide UltimateLeaderboard gates this via
/// CountNPCKillsAsPlayerKills inside CanDamage(..., npc: true).
/// </summary>
[HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
public static class Patch_BaseCombatEntity_Hurt
{
    static void Postfix(BaseCombatEntity __instance, HitInfo info)
    {
        if (info?.HitEntity != __instance) return;
        if (__instance is not BasePlayer victim) return;

        var attacker = info.InitiatorPlayer;
        if (attacker == null || attacker.IsNpc || attacker == victim) return;
        if (!SteamIdHelper.IsSteamId(attacker.userID)) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        bool victimIsHuman = SteamIdHelper.IsSteamId(victim.userID);
        if (!victimIsHuman && mod.GetConfig()?.CountNpcHitsForHitrate != true)
            return;

        // Only count actual damage (e.g. skip 0-damage or prediction)
        if (!info.hasDamage) return;

        string key = HitAreaToKey(info.boneArea);
        if (string.IsNullOrEmpty(key)) return;

        mod.RecordStat(attacker.userID, LootType.BodyHits, key, 1f);
    }

    /// <summary>Map HitArea to our storage key (head, chest, stomach, arm, leg).</summary>
    private static string HitAreaToKey(HitArea area)
    {
        if ((area & HitArea.Head) != 0) return "head";
        if ((area & HitArea.Chest) != 0) return "chest";
        if ((area & HitArea.Stomach) != 0) return "stomach";
        if ((area & HitArea.Arm) != 0 || (area & HitArea.Hand) != 0) return "arm";
        if ((area & HitArea.Leg) != 0 || (area & HitArea.Foot) != 0) return "leg";
        return null;
    }
}
