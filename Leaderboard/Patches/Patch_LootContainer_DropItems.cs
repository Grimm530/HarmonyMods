using System.Collections.Generic;
using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(LootContainer), nameof(LootContainer.DropItems), new[] { typeof(BaseEntity) })]
public static class Patch_LootContainer_DropItems
{
    static void Prefix(LootContainer __instance, BaseEntity initiator, out List<(string shortname, int amount)> __state)
    {
        __state = null;
        if (initiator is not BasePlayer player || player.IsNpc || !SteamIdHelper.IsSteamId(player.userID)) return;
        if (__instance?.inventory?.itemList == null) return;
        // Only track barrel-style drops (matches UltimateLeaderboard OnContainerDropItems)
        if (string.IsNullOrEmpty(__instance.ShortPrefabName) || !__instance.ShortPrefabName.Contains("barrel")) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        __state = new List<(string, int)>();
        foreach (var item in __instance.inventory.itemList)
        {
            if (item?.info == null || string.IsNullOrEmpty(item.info.shortname)) continue;
            __state.Add((item.info.shortname, item.amount));
        }
    }

    static void Postfix(List<(string shortname, int amount)> __state, BaseEntity initiator)
    {
        if (__state == null || __state.Count == 0 || initiator is not BasePlayer player) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        foreach (var (shortname, amount) in __state)
            mod.RecordStat(player.userID, LootType.LootItems, shortname, amount);
    }
}
