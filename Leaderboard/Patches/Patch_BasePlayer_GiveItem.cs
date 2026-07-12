using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.GiveItem), new[] { typeof(Item), typeof(BaseEntity.GiveItemReason), typeof(GiveItemOptions) })]
public static class Patch_BasePlayer_GiveItem
{
    static void Postfix(BasePlayer __instance, Item item, BaseEntity.GiveItemReason reason)
    {
        if (__instance == null || item?.info == null) return;
        if (!SteamIdHelper.IsSteamId(__instance.userID)) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        var shortname = item.info.shortname ?? "";
        if (reason == BaseEntity.GiveItemReason.ResourceHarvested)
            mod.RecordStat(__instance.userID, LootType.Gather, shortname, item.amount);
        else if (reason == BaseEntity.GiveItemReason.Crafted && IsFishShortname(shortname))
            mod.RecordStat(__instance.userID, LootType.Fishing, shortname, item.amount);
    }

    private static bool IsFishShortname(string shortname)
    {
        if (string.IsNullOrEmpty(shortname)) return false;
        return shortname.StartsWith("fish.", System.StringComparison.OrdinalIgnoreCase)
               || shortname == "skull.human"; // fishing can give this
    }
}
