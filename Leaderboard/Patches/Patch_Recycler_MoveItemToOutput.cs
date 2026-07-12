using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(Recycler), nameof(Recycler.MoveItemToOutput), new[] { typeof(Item) })]
public static class Patch_Recycler_MoveItemToOutput
{
    static void Postfix(Recycler __instance, Item newItem)
    {
        if (__instance == null || newItem?.info == null) return;
        var player = (__instance as StorageContainer)?.LastLootedByPlayer;
        if (player == null || !SteamIdHelper.IsSteamId(player.userID)) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        mod.RecordStat(player.userID, LootType.RecycleItem, newItem.info.shortname ?? "", newItem.amount);
    }
}
