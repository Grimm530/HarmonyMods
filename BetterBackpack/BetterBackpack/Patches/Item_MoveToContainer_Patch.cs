using System.Collections.Generic;
using HarmonyLib;

namespace BetterBackpack;

/// <summary>
/// Existing (pickup only): mark world/loot → main/belt moves so OnItemAddedOrRemoved
/// can gather into the worn backpack. Do not change the destination — vanilla GiveItem /
/// MoveItem must keep failing when inventory is full (item stays in the crate).
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer), typeof(ItemContainer), typeof(int), typeof(bool), typeof(bool), typeof(BasePlayer), typeof(bool))]
internal class Item_MoveToContainer_Patch
{
    /// <summary>Item UIDs currently moving from world/external loot into main or belt.</summary>
    internal static readonly HashSet<ulong> ExternalPickupItemUids = new HashSet<ulong>();

    internal struct MoveLogState
    {
        public bool Track;
        public string From;
        public bool ToBag;
    }

    [HarmonyPrefix]
    private static void Prefix(Item __instance, ItemContainer newcontainer, BasePlayer sourcePlayer, ref MoveLogState __state)
    {
        __state = default;
        if (__instance == null || newcontainer == null) return;

        var destOwner = newcontainer.playerOwner;
        if (destOwner?.inventory != null)
        {
            var inv = destOwner.inventory;
            if ((newcontainer == inv.containerMain || newcontainer == inv.containerBelt)
                && IsExternalSource(__instance, destOwner))
            {
                lock (ExternalPickupItemUids)
                    ExternalPickupItemUids.Add(__instance.uid.Value);
                if (LootDebug.ShouldLog(destOwner))
                    LootDebug.Log(destOwner, $"PickupMark {LootDebug.ItemDesc(__instance)} from={LootDebug.ContainerDesc(__instance.parent, destOwner)} dest={LootDebug.ContainerDesc(newcontainer, destOwner)}");
            }
        }

        if (!LootDebug.IsActive) return;
        var player = LootDebug.ResolvePlayer(newcontainer, sourcePlayer, __instance);
        if (!LootDebug.ShouldLog(player)) return;

        var fromExternal = LootDebug.IsExternalLoot(__instance.parent, player);
        var toBag = LootDebug.IsPlayerBackpack(newcontainer, player);
        if (!fromExternal && !toBag) return;

        __state.Track = true;
        __state.ToBag = toBag;
        __state.From = LootDebug.ContainerDesc(__instance.parent, player);
    }

    [HarmonyPostfix]
    private static void Postfix(Item __instance, ItemContainer newcontainer, int iTargetPos, bool __result, BasePlayer sourcePlayer, MoveLogState __state)
    {
        if (__instance == null) return;

        if (__state.Track)
        {
            var player = LootDebug.ResolvePlayer(newcontainer, sourcePlayer, __instance);
            if (LootDebug.ShouldLog(player))
            {
                var gone = !__instance.IsValid() || __instance.parent == null;
                string after;
                if (!__instance.IsValid())
                    after = "GONE";
                else
                    after = $"parent={LootDebug.ContainerDesc(__instance.parent, player)} pos={__instance.position} amt={__instance.amount}";
                var flag = !__result && gone ? " PARENTLESS/DELETED" : "";
                LootDebug.Log(player, $"MoveToContainer {(__result ? "OK" : "FAIL")} {LootDebug.ItemDesc(__instance)} from={__state.From} dest={LootDebug.ContainerDesc(newcontainer, player)} slot={iTargetPos} toBag={__state.ToBag} | after {after}{flag}");
            }
        }

        // Keep the mark on a failed move so GiveItem can retry another container.
        // Keep it on a successful land in main/belt so OnItemAddedOrRemoved can consume it.
        // Clear it when the item stacked-away or landed somewhere else (backpack overflow).
        if (!__result) return;
        var landedOwner = newcontainer?.playerOwner;
        var landedInv = landedOwner?.inventory;
        if (landedInv != null && __instance.IsValid()
            && (__instance.parent == landedInv.containerMain || __instance.parent == landedInv.containerBelt))
            return;
        lock (ExternalPickupItemUids)
            ExternalPickupItemUids.Remove(__instance.uid.Value);
    }

    internal static bool ConsumeExternalPickupMark(Item item)
    {
        if (item == null) return false;
        lock (ExternalPickupItemUids)
            return ExternalPickupItemUids.Remove(item.uid.Value);
    }

    /// <summary>
    /// Crate/barrel loot and world pickups only. SplitItem, slot swaps, and dragging
    /// out of a backpack all pass through MoveToContainer with parent == null (or from
    /// a bag) and must not be treated as loot — Existing would yank them back.
    /// </summary>
    internal static bool IsExternalSource(Item item, BasePlayer player)
    {
        if (player?.inventory == null || item == null)
            return false;

        if (IsLootingOwnBackpack(player))
            return false;

        var parent = item.parent;
        if (parent == null)
            return item.GetWorldEntity() != null;

        var inv = player.inventory;
        if (parent == inv.containerMain || parent == inv.containerBelt || parent == inv.containerWear)
            return false;

        if (IsPlayerManagedBackpackContainer(parent, player))
            return false;

        return true;
    }

    internal static bool IsLootingOwnBackpack(BasePlayer player)
    {
        var containers = player?.inventory?.loot?.containers;
        if (containers == null || containers.Count == 0)
            return false;
        return IsPlayerManagedBackpackContainer(containers[0], player);
    }

    internal static bool IsPlayerManagedBackpackContainer(ItemContainer container, BasePlayer player)
    {
        var current = container;
        for (int depth = 0; depth < 8 && current != null; depth++)
        {
            if (VirtualBackpackApi.IsVirtualBackpackContainer(current))
                return true;

            var worn = player?.inventory?.GetBackpackWithInventory();
            if (worn?.contents != null && current == worn.contents)
                return true;

            current = current.parent?.parent;
        }
        return false;
    }

    internal static void MarkExternalPickup(Item item)
    {
        if (item == null) return;
        lock (ExternalPickupItemUids)
            ExternalPickupItemUids.Add(item.uid.Value);
    }

    internal static void ClearExternalPickupMark(Item item)
    {
        if (item == null) return;
        lock (ExternalPickupItemUids)
            ExternalPickupItemUids.Remove(item.uid.Value);
    }
}
