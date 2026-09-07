using System.Collections.Generic;
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
    private static readonly HashSet<ulong> ExternalPickupMarks = new();

    internal static void MarkExternalPickup(Item item)
    {
        if (item?.uid != null)
            ExternalPickupMarks.Add(item.uid.Value);
    }

    internal static void ClearExternalPickupMark(Item item)
    {
        if (item?.uid != null)
            ExternalPickupMarks.Remove(item.uid.Value);
    }

    internal static bool IsExternalPickup(Item item) =>
        item?.uid != null && ExternalPickupMarks.Contains(item.uid.Value);

    internal static bool IsPlayerManagedBackpackContainer(ItemContainer container, BasePlayer player)
    {
        if (container == null || player?.inventory == null)
            return false;
        var bag = player.inventory.GetBackpackWithInventory();
        return bag != null && container == bag.contents;
    }

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
