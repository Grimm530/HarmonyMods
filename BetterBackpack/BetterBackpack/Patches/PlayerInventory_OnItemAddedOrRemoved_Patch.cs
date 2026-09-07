using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When an item is added to main or belt and Existing is enabled, try to stack it into the backpack
/// if the backpack already has a matching stack with room. Moves are deferred to the next tick to
/// avoid re-entrancy (e.g. when gathering with GatherManager causes NullRef in ItemContainer.Remove).
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.OnItemAddedOrRemoved))]
internal class PlayerInventory_OnItemAddedOrRemoved_Patch
{
    private static readonly Queue<PendingMove> DeferredMoves = new Queue<PendingMove>();
    private const int MaxDeferredPerTick = 5;
    /// <summary>1 when the queue may contain work; lets ServerMgr.Update skip lock+ dequeue when idle.</summary>
    private static int _deferredWorkHint;

    private struct PendingMove
    {
        public PlayerInventory Inventory;
        public Item Item;
        public ItemContainer BackpackContainer;
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, Item item, bool bAdded)
    {
        if (!bAdded || item == null) return;
        var player = __instance.GetComponent<BasePlayer>();
        if (player == null || player.IsDead() || player.IsSleeping()) return;

        if (item.IsBackpack()) return;

        var parent = item.parent;
        if (parent != __instance.containerMain && parent != __instance.containerBelt) return;

        if (!(BetterBackpackConfig.Config?.ExistingEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.ExistingEnabled) return;

        if (IsLootingBackpack(player)) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        var backpackContainer = backpack.contents;
        var existingInBackpack = backpackContainer.FindItemByItemID(item.info.itemid);
        if (existingInBackpack == null) return;

        if (item.info.stackable <= 1) return;
        if (existingInBackpack.amount >= existingInBackpack.MaxStackable()) return;

        // Defer move to next tick to avoid re-entrancy during Insert (fixes NullRef when jackhammering with GatherManager).
        lock (DeferredMoves)
        {
            DeferredMoves.Enqueue(new PendingMove { Inventory = __instance, Item = item, BackpackContainer = backpackContainer });
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
                if (pm.Item == null || !pm.Item.IsValid()) continue;
                if (pm.Inventory == null || pm.BackpackContainer == null) continue;
                var parent = pm.Item.parent;
                if (parent != pm.Inventory.containerMain && parent != pm.Inventory.containerBelt) continue;
                pm.Item.MoveToContainer(pm.BackpackContainer, -1, allowStack: true);
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

    private static bool IsLootingBackpack(BasePlayer player)
    {
        var loot = player?.inventory?.loot;
        if (loot == null || loot.containers == null || loot.containers.Count == 0) return false;
        var first = loot.containers[0];
        return first?.parent?.info?.GetComponent<ItemModBackpack>() != null;
    }
}
