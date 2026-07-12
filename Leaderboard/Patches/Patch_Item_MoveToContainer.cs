using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// When a player moves an item from a loot container or storage container (crate/box) into their
/// inventory, record it as LootItems. This detects manual looting from crates and boxes; barrel
/// loot is still handled by WorldItem.Pickup (InstantBarrel) and LootContainer.DropItems (vanilla).
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer), new[]
{
    typeof(ItemContainer),
    typeof(int),
    typeof(bool),
    typeof(bool),
    typeof(BasePlayer),
    typeof(bool)
})]
public static class Patch_Item_MoveToContainer
{
    static void Prefix(Item __instance, ItemContainer newcontainer, BasePlayer sourcePlayer,
        out (ulong userId, string shortname, int amount)? __state)
    {
        __state = null;
        if (__instance?.info == null || string.IsNullOrEmpty(__instance.info.shortname)) return;
        var source = __instance.parent;
        if (source?.entityOwner == null || newcontainer?.playerOwner == null) return;
        // Only count when moving FROM a world/deployable container TO a player's inventory
        if (!(source.entityOwner is StorageContainer)) return;
        var player = newcontainer.playerOwner;
        if (player == null || player.IsNpc || !SteamIdHelper.IsSteamId(player.userID)) return;
        __state = (player.userID, __instance.info.shortname, __instance.amount);
    }

    static void Postfix((ulong userId, string shortname, int amount)? __state)
    {
        if (__state == null) return;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return;

        mod.RecordStat(__state.Value.userId, LootType.LootItems, __state.Value.shortname, __state.Value.amount);
    }
}
