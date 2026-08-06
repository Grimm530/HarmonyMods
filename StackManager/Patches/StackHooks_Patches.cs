using System;
using HarmonyLib;
using UnityEngine;

namespace StackManagerHarmony.Patches
{
    /// <summary>Oxide CanStackItem → Item.CanStack</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.CanStack))]
    public static class Item_CanStack_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, Item item, ref bool __result)
        {
            try
            {
                var plugin = StackManagerHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                var hook = plugin.CanStackItem(__instance, item);
                if (hook is bool b)
                {
                    __result = b;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] CanStackItem: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnMaxStackable → Item.MaxStackable</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.MaxStackable))]
    public static class Item_MaxStackable_Patch
    {
        private static float _nextErrorLogAt;

        [HarmonyPrefix]
        public static bool Prefix(Item __instance, ref int __result)
        {
            try
            {
                var plugin = StackManagerHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                var hook = plugin.OnMaxStackable(__instance);
                if (hook is int i)
                {
                    __result = i;
                    return false;
                }
            }
            catch (Exception ex)
            {
                // MaxStackable is called heavily during map spawn — rate-limit to avoid console floods.
                float now = Time.realtimeSinceStartup;
                if (now >= _nextErrorLogAt)
                {
                    _nextErrorLogAt = now + 5f;
                    string itemLabel = __instance == null
                        ? "item=null"
                        : (__instance.info == null
                            ? $"itemuid={__instance.uid} info=null"
                            : $"item={__instance.info.shortname} amount={__instance.amount}");
                    string parentLabel = __instance?.parent == null
                        ? "parent=null"
                        : $"parentFlags={(int)__instance.parent.flags} owner={__instance.parent.entityOwner?.ShortPrefabName ?? "none"}";
                    Debug.LogWarning($"[StackManager] OnMaxStackable failed ({ex.GetType().Name}): {ex.Message} | {itemLabel} | {parentLabel}\n{ex.StackTrace}");
                }
            }
            return true;
        }
    }

    /// <summary>Oxide OnItemSplit → Item.SplitItem</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.SplitItem))]
    public static class Item_SplitItem_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, int split_Amount, ref Item __result)
        {
            try
            {
                var plugin = StackManagerHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                var hook = plugin.OnItemSplit(__instance, split_Amount);
                if (hook is Item splitItem)
                {
                    __result = splitItem;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnItemSplit: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnItemAction → Item.ServerCommand</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.ServerCommand))]
    public static class Item_ServerCommand_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, string command, BasePlayer player)
        {
            try
            {
                var plugin = StackManagerHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                var hook = plugin.OnItemAction(__instance, command);
                if (hook is bool b && !b)
                    return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnItemAction: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide CanMoveItem → PlayerInventory.MoveItem (RPC). Rewinds NetRead if hook returns null.</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.MoveItem))]
    public static class PlayerInventory_MoveItem_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInventory __instance, BaseEntity.RPCMessage msg)
        {
            try
            {
                var plugin = StackManagerHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                var read = msg.read;
                if (read == null) return true;

                long pos = read.Position;
                try
                {
                    ItemId itemId = read.ItemID();
                    ItemContainerId targetContainer = read.ItemContainerID();
                    int targetSlot = read.Int8();
                    uint amount = read.UInt32();
                    ItemMoveModifier modifiers = (ItemMoveModifier)read.Int32();

                    Item item = __instance.FindItemByUID(itemId);
                    if (item == null)
                        return true;

                    var hook = plugin.CanMoveItem(item, __instance, targetContainer, targetSlot, (int)amount, modifiers);
                    if (hook != null)
                        return false; // plugin handled / blocked
                }
                finally
                {
                    try { read.Position = pos; } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] CanMoveItem: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide CanMoveItemsFrom → PlayerInventory.CanMoveItemsFrom</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.CanMoveItemsFrom))]
    public static class PlayerInventory_CanMoveItemsFrom_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInventory __instance, BaseEntity entity, Item item, ref PlayerInventory.CanMoveFromResponse __result)
        {
            try
            {
                var plugin = StackManagerHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                // Non-storage sources must fall through to vanilla. Only override when
                // a StorageContainer explicitly denies the move (keeps deny reason text).
                if (entity is not StorageContainer)
                    return true;

                if (!plugin.TryGetCanMoveItemsFrom(__instance, entity, item, out var response))
                {
                    __result = response;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] CanMoveItemsFrom: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnGiveSoldItem → VendingMachine.GiveSoldItem</summary>
    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.GiveSoldItem))]
    public static class VendingMachine_GiveSoldItem_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(VendingMachine __instance, Item soldItem, BasePlayer buyer)
        {
            try
            {
                var plugin = StackManagerHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                var hook = plugin.OnGiveSoldItem(__instance, soldItem, buyer);
                if (hook != null)
                    return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnGiveSoldItem: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnEntityBuilt → Planner.DoBuild</summary>
    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__result == null) return;
            try
            {
                StackManagerHarmonyMod.Instance?.Plugin?.OnEntityBuilt(__instance, __result.gameObject);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnEntityBuilt: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnLootEntity → PlayerLoot.StartLootingEntity</summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity)
        {
            try
            {
                var player = __instance?.baseEntity;
                if (player == null) return;
                if (targetEntity is StorageContainer storage)
                    StackManagerHarmonyMod.Instance?.Plugin?.OnLootEntity(player, storage);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnLootEntity: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnPlayerRespawned → BasePlayer.RespawnAt</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    public static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                StackManagerHarmonyMod.Instance?.Plugin?.OnPlayerRespawned(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnPlayerRespawned: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnPlayerConnected → BasePlayer.PlayerInit</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                StackManagerHarmonyMod.Instance?.Plugin?.OnPlayerConnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnPlayerConnected: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnPlayerDisconnected → BasePlayer.OnDisconnected</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                StackManagerHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }
}
