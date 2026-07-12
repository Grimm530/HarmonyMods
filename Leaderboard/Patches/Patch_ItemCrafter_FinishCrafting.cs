using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(ItemCrafter), "FinishCrafting", new[] { typeof(ItemCraftTask) })]
public static class Patch_ItemCrafter_FinishCrafting
{
    static void Postfix(ItemCrafter __instance, ItemCraftTask task)
    {
        if (__instance?.owner == null || task?.blueprint?.targetItem == null) return;
        var player = __instance.owner;
        if (!SteamIdHelper.IsSteamId(player.userID)) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        var shortname = task.blueprint.targetItem.shortname ?? "";
        var amount = task.blueprint?.amountToCreate ?? 1;
        mod.RecordStat(player.userID, LootType.Craft, shortname, amount);
    }
}
