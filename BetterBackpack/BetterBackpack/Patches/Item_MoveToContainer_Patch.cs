using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When hover/quick-loot sends items to main with a specific container target,
/// redirect to backpack when Existing is on and backpack has a matching stack.
/// Fixes: hover loot not stacking to backpack when client explicitly targets main.
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer), typeof(ItemContainer), typeof(int), typeof(bool), typeof(bool), typeof(BasePlayer), typeof(bool))]
internal class Item_MoveToContainer_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Item __instance, ref ItemContainer newcontainer)
    {
        if (newcontainer == null) return;
        var player = newcontainer.playerOwner;
        if (player?.inventory == null) return;
        if (newcontainer != player.inventory.containerMain) return;

        // Only redirect when item is from external loot (crate, stash, etc.)
        var parent = __instance.parent;
        if (parent == null) return;
        if (parent == player.inventory.containerMain || parent == player.inventory.containerBelt ||
            parent == player.inventory.containerWear) return;
        var backpack = player.inventory.GetBackpackWithInventory();
        if (backpack != null && parent == backpack.contents) return;

        if (__instance.info == null || __instance.info.stackable <= 1) return;
        if (!(BetterBackpackConfig.Config?.ExistingEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.ExistingEnabled) return;

        if (backpack?.contents == null) return;
        var existingInBackpack = backpack.contents.FindItemByItemID(__instance.info.itemid);
        if (existingInBackpack == null) return;
        if (existingInBackpack.amount >= existingInBackpack.MaxStackable()) return;

        newcontainer = backpack.contents;
    }
}
