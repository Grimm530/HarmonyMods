using System;
using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(Recycler), nameof(Recycler.MoveItemToOutput), new[] { typeof(Item) })]
public static class Patch_Recycler_MoveItemToOutput
{
    static void Postfix(Recycler __instance, Item newItem)
    {
        try
        {
            if (newItem?.info == null || newItem.amount <= 0) return;

            ulong userId = 0;
            if (__instance?.net != null && __instance.net.ID.IsValid &&
                CombatTracking.TryGetRecyclerPlayer(__instance.net.ID.Value, out var mapped))
                userId = mapped;

            if (userId == 0)
            {
                var player = __instance?.LastLootedByPlayer;
                if (player != null && !player.IsNpc && SteamIdHelper.IsSteamId(player.userID))
                    userId = player.userID;
            }

            if (userId == 0) return;
            var shortname = newItem.info.shortname;
            if (string.IsNullOrEmpty(shortname)) return;
            LeaderboardMod.Instance?.RecordStat(userId, LootType.RecycleItem, shortname, newItem.amount);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Leaderboard] Recycle output: " + ex.Message);
        }
    }
}

/// <summary>Remember who switched the recycler on. Output can be produced after they close the loot panel.</summary>
[HarmonyPatch(typeof(Recycler), "SVSwitch")]
public static class Patch_Recycler_SVSwitch
{
    static void Postfix(Recycler __instance, BaseEntity.RPCMessage msg)
    {
        try
        {
            if (__instance?.net == null || !__instance.net.ID.IsValid) return;
            var id = __instance.net.ID.Value;
            if (!__instance.IsOn())
            {
                CombatTracking.ClearRecyclerPlayer(id);
                return;
            }

            var player = msg.player;
            if (player != null && !player.IsNpc && SteamIdHelper.IsSteamId(player.userID))
                CombatTracking.SetRecyclerPlayer(id, player.userID);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[Leaderboard] Recycler toggle: " + ex.Message);
        }
    }
}
