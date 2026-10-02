using System;
using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
public static class Patch_BaseCombatEntity_Die
{
    static void Postfix(BaseCombatEntity __instance, HitInfo info)
    {
        var mod = LeaderboardMod.Instance;
        if (mod == null || __instance == null) return;

        // Player death, including scientist NPCs, is handled in Patch_BasePlayer_Die.
        if (__instance is BasePlayer) return;

        try
        {
            if (__instance is PatrolHelicopter heli)
            {
                ulong id = 0;
                var killer = info?.InitiatorPlayer;
                if (killer != null && !killer.IsNpc && SteamIdHelper.IsSteamId(killer.userID))
                    id = killer.userID;
                else
                    CombatTracking.TryConsumeHeliAttacker(heli, out id);
                CombatTracking.ForgetHeli(heli);

                if (id != 0 && mod.TryGetStats(id, out _))
                    mod.RecordStat(id, LootType.Kill, "helicopter", 1f);
                return;
            }

            var attacker = info?.InitiatorPlayer;
            if (attacker == null || attacker.IsNpc || !SteamIdHelper.IsSteamId(attacker.userID)) return;
            if (!mod.TryGetStats(attacker.userID, out _)) return;

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
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Leaderboard] Entity death: " + ex.Message);
        }
    }
}
