using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// Existing (pickup only):
/// 1) When hover/quick-loot targets main from an external container, redirect to backpack
///    if backpack already has a matching stack with room.
/// 2) Mark external→main/belt moves so OnItemAddedOrRemoved can auto-stack without
///    pulling items the player moved out of their own inventory (backpack/main/belt/wear).
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer), typeof(ItemContainer), typeof(int), typeof(bool), typeof(bool), typeof(BasePlayer), typeof(bool))]
internal class Item_MoveToContainer_Patch
{
    /// <summary>Item UIDs currently moving from world/external loot into main or belt.</summary>
    internal static readonly HashSet<ulong> ExternalPickupItemUids = new HashSet<ulong>();

    [HarmonyPrefix]
    private static void Prefix(Item __instance, ref ItemContainer newcontainer)
    {
        if (__instance == null || newcontainer == null) return;
        var player = newcontainer.playerOwner;
        if (player?.inventory == null) return;

        var inv = player.inventory;
        var toMainOrBelt = newcontainer == inv.containerMain || newcontainer == inv.containerBelt;
        if (!toMainOrBelt) return;

        var parent = __instance.parent;
        if (!IsExternalSource(parent, player)) return;

        // Redirect hover/quick-loot into backpack when Existing can stack.
        if (newcontainer == inv.containerMain
            && __instance.info != null
            && __instance.info.stackable > 1
            && (BetterBackpackConfig.Config?.ExistingEnabled ?? true))
        {
            var mod = BetterBackpackMod.Instance;
            var prefs = mod?.GetOrCreatePrefs(player);
            if (prefs != null && prefs.ExistingEnabled)
            {
                var backpack = inv.GetBackpackWithInventory();
                if (backpack?.contents != null)
                {
                    var existingInBackpack = backpack.contents.FindItemByItemID(__instance.info.itemid);
                    if (existingInBackpack != null
                        && existingInBackpack.amount < existingInBackpack.MaxStackable())
                    {
                        newcontainer = backpack.contents;
                        return;
                    }
                }
            }
        }

        // Only mark when the item will still land in main/belt (pickup leftovers for deferred stack).
        lock (ExternalPickupItemUids)
            ExternalPickupItemUids.Add(__instance.uid.Value);
    }

    [HarmonyPostfix]
    private static void Postfix(Item __instance, bool __result)
    {
        if (__instance == null || __result) return;
        // Move failed — drop the pickup mark so it cannot leak into a later add.
        lock (ExternalPickupItemUids)
            ExternalPickupItemUids.Remove(__instance.uid.Value);
    }

    internal static bool ConsumeExternalPickupMark(Item item)
    {
        if (item == null) return false;
        lock (ExternalPickupItemUids)
            return ExternalPickupItemUids.Remove(item.uid.Value);
    }

    private static bool IsExternalSource(ItemContainer parent, BasePlayer player)
    {
        // null parent = world pickup / freshly created loot item
        if (parent == null) return true;

        var inv = player.inventory;
        if (parent == inv.containerMain || parent == inv.containerBelt || parent == inv.containerWear)
            return false;

        var backpack = inv.GetBackpackWithInventory();
        if (backpack != null && parent == backpack.contents)
            return false;

        return true;
    }
}
