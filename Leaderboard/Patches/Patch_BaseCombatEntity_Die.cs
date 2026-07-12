using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
public static class Patch_BaseCombatEntity_Die
{
    static void Postfix(BaseCombatEntity __instance, HitInfo info)
    {
        if (__instance == null) return;
        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        // Player death is handled in Patch_BasePlayer_Die
        if (__instance is BasePlayer) return;

        var attacker = info?.InitiatorPlayer;
        if (attacker == null || attacker.IsNpc || !SteamIdHelper.IsSteamId(attacker.userID)) return;
        if (!LeaderboardMod.Instance.TryGetStats(attacker.userID, out _)) return;

        if (__instance is PatrolHelicopter)
        {
            mod.RecordStat(attacker.userID, LootType.Kill, "helicopter", 1f);
            return;
        }
        if (__instance is BradleyAPC)
        {
            mod.RecordStat(attacker.userID, LootType.Kill, "bradleyapc", 1f);
            return;
        }
        if (__instance is BuildingBlock block)
        {
            mod.RecordStat(attacker.userID, LootType.Raid, block.ShortPrefabName ?? "building", 1f);
            return;
        }

        var prefab = __instance.ShortPrefabName;
        if (string.IsNullOrEmpty(prefab)) prefab = "entity";
        mod.RecordStat(attacker.userID, LootType.Kill, prefab, 1f);
    }
}
