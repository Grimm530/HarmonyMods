using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// When a player picks up a world item (e.g. from ground or from InstantBarrel drop), record as LootItems
/// so scrap and other pickups are counted. Captures item in Prefix (before RemoveItem in Pickup).
/// </summary>
[HarmonyPatch(typeof(WorldItem), nameof(WorldItem.Pickup), new[] { typeof(BaseEntity.RPCMessage) })]
public static class Patch_WorldItem_Pickup
{
    static void Prefix(WorldItem __instance, BaseEntity.RPCMessage msg, out (ulong userId, string shortname, int amount)? __state)
    {
        __state = null;
        if (__instance?.item?.info == null || msg.player == null) return;
        if (string.IsNullOrEmpty(__instance.item.info.shortname)) return;
        if (!SteamIdHelper.IsSteamId(msg.player.userID)) return;

        __state = (msg.player.userID, __instance.item.info.shortname, __instance.item.amount);
    }

    static void Postfix((ulong userId, string shortname, int amount)? __state)
    {
        if (__state == null) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        mod.RecordStat(__state.Value.userId, LootType.LootItems, __state.Value.shortname, __state.Value.amount);
    }
}
