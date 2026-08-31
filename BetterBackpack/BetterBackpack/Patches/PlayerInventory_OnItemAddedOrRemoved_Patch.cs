using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// Existing: after loot/pickup lands in main or belt, yank it into a worn Rust backpack
/// or virtual Backpacks bag if that item already exists there. Stack if there is room,
/// otherwise take an empty slot. If the bag has no space, leave the item in inventory.
/// Does not change crate loot destinations — full main stays vanilla (item stays in crate).
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.OnItemAddedOrRemoved))]
internal class PlayerInventory_OnItemAddedOrRemoved_Patch
{
    private static readonly Queue<PendingMove> DeferredMoves = new Queue<PendingMove>();
    private const int MaxDeferredPerTick = 12;
    private static int _deferredWorkHint;

    private struct PendingMove
    {
        public PlayerInventory Inventory;
        public Item Item;
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, Item item, bool bAdded)
    {
        if (!bAdded || item == null) return;
        var player = __instance.GetComponent<BasePlayer>();
        if (player == null || player.IsDead() || player.IsSleeping()) return;

        if (item.IsBackpack())
        {
            if (item.parent == __instance.containerWear)
                ItemRetrieverSupplier.HideFromItemRetrieverWalk(item);
            return;
        }

        var parent = item.parent;
        if (parent != __instance.containerMain && parent != __instance.containerBelt) return;

        if (!Item_MoveToContainer_Patch.ConsumeExternalPickupMark(item)) return;

        if (Item_MoveToContainer_Patch.IsLootingOwnBackpack(player))
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing SKIP looting-backpack {LootDebug.ItemDesc(item)}");
            return;
        }

        if (!(BetterBackpackConfig.Config?.ExistingEnabled ?? true)
            || BetterBackpackMod.Instance?.GetOrCreatePrefs(player)?.ExistingEnabled != true)
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing SKIP disabled {LootDebug.ItemDesc(item)} | {LootDebug.InvSnap(player)}");
            return;
        }

        if (item.info == null)
            return;

        var worn = __instance.GetBackpackWithInventory()?.contents;
        var wornHas = ContainerHasItem(worn, item);
        var virtualHas = VirtualBackpackApi.HasMatchingItem(player, item);
        if (!wornHas && !virtualHas)
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing SKIP not-in-backpack {LootDebug.ItemDesc(item)}");
            return;
        }

        if (LootDebug.ShouldLog(player))
            LootDebug.Log(player, $"Existing QUEUE {LootDebug.ItemDesc(item)} wornHas={wornHas} virtualHas={virtualHas} | {LootDebug.InvSnap(player)}");

        lock (DeferredMoves)
        {
            DeferredMoves.Enqueue(new PendingMove { Inventory = __instance, Item = item });
        }

        Interlocked.Exchange(ref _deferredWorkHint, 1);
    }

    internal static void ProcessDeferredMoves()
    {
        if (Volatile.Read(ref _deferredWorkHint) == 0)
            return;

        int n = 0;
        while (n < MaxDeferredPerTick)
        {
            PendingMove pm;
            lock (DeferredMoves)
            {
                if (DeferredMoves.Count == 0)
                    break;
                pm = DeferredMoves.Dequeue();
            }

            n++;
            try
            {
                TryGatherDeferred(pm);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        lock (DeferredMoves)
        {
            if (DeferredMoves.Count > 0)
                Interlocked.Exchange(ref _deferredWorkHint, 1);
            else
                Interlocked.Exchange(ref _deferredWorkHint, 0);
        }
    }

    private static void TryGatherDeferred(PendingMove pm)
    {
        var player = pm.Inventory != null ? pm.Inventory.baseEntity : null;
        if (pm.Item == null || !pm.Item.IsValid())
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, "Existing deferred GONE before move (item invalid)");
            return;
        }
        if (pm.Inventory == null) return;

        var parent = pm.Item.parent;
        if (parent != pm.Inventory.containerMain && parent != pm.Inventory.containerBelt)
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing deferred SKIP relocated {LootDebug.ItemDesc(pm.Item)} parent={LootDebug.ContainerDesc(parent, player)}");
            return;
        }

        if (Item_MoveToContainer_Patch.IsLootingOwnBackpack(player))
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing deferred SKIP looting-backpack {LootDebug.ItemDesc(pm.Item)}");
            return;
        }

        var beforeParent = LootDebug.ContainerDesc(pm.Item.parent, player);
        var worn = pm.Inventory.GetBackpackWithInventory()?.contents;
        var wornHas = ContainerHasItem(worn, pm.Item);
        var virtualHas = VirtualBackpackApi.HasMatchingItem(player, pm.Item);

        if (wornHas && worn != null && pm.Item.MoveToContainer(worn, -1, allowStack: true))
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing deferred OK worn {LootDebug.ItemDesc(pm.Item)} from={beforeParent} dest={LootDebug.ContainerDesc(pm.Item.parent, player)}");
            return;
        }

        if (pm.Item.IsValid() && pm.Item.parent == null)
        {
            DropOrStay(pm.Item, player, "worn insert parentless");
            return;
        }

        if (virtualHas && VirtualBackpackApi.TryDeposit(player, pm.Item))
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing deferred OK virtual {LootDebug.ItemDesc(pm.Item)} from={beforeParent}");
            return;
        }

        if (pm.Item.IsValid() && pm.Item.parent == null)
        {
            DropOrStay(pm.Item, player, "virtual insert parentless");
            return;
        }

        if (LootDebug.ShouldLog(player))
            LootDebug.Log(player, $"Existing deferred FAIL stayed in inventory {LootDebug.ItemDesc(pm.Item)} parent={LootDebug.ContainerDesc(pm.Item.parent, player)}");
    }

    private static void DropOrStay(Item item, BasePlayer player, string reason)
    {
        if (player != null && !player.IsDestroyed)
        {
            if (LootDebug.ShouldLog(player))
                LootDebug.Log(player, $"Existing deferred FAIL {reason} → DROP {LootDebug.ItemDesc(item)}");
            item.Drop(player.GetDropPosition(), player.GetDropVelocity());
        }
    }

    private static bool ContainerHasItem(ItemContainer container, Item item)
    {
        if (container?.itemList == null || item?.info == null) return false;
        var list = container.itemList;
        for (int i = 0; i < list.Count; i++)
        {
            var other = list[i];
            if (other == null || other == item || other.info == null) continue;
            if (other.info.itemid == item.info.itemid)
                return true;
        }
        return false;
    }
}
