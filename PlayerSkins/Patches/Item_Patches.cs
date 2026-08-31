using System;
using HarmonyLib;
using UnityEngine;

namespace PlayerSkinsHarmony.Patches
{
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.CanAcceptItem))]
    internal static class ItemContainer_CanAcceptItem_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemContainer __instance, BasePlayer player, Item item, int targetPos, ref ItemContainer.CanAcceptResult __result)
        {
            if (!PlayerSkinsPlugin.ReskinLootHooksActive) return;
            if (__result != ItemContainer.CanAcceptResult.CanAccept) return;
            var plugin = PlayerSkinsMod.Instance?.Plugin;
            if (plugin == null) return;
            try
            {
                var hookResult = plugin.CanAcceptItem(__instance, item);
                if (hookResult is ItemContainer.CanAcceptResult result)
                    __result = result;
                else if (hookResult is bool b && !b)
                    __result = ItemContainer.CanAcceptResult.CannotAccept;
            }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] CanAcceptItem: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer))]
    internal static class Item_MoveToContainer_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Item __instance, ItemContainer newcontainer, int iTargetPos, bool allowStack,
            bool ignoreStackLimit, BasePlayer sourcePlayer, bool allowSwap)
        {
            if (!PlayerSkinsPlugin.ReskinLootHooksActive) return true;
            var plugin = PlayerSkinsMod.Instance?.Plugin;
            if (plugin == null || sourcePlayer?.inventory == null) return true;
            try
            {
                var targetId = newcontainer?.uid ?? default;
                var amount = __instance?.amount ?? 0;
                var blocked = plugin.CanMoveItem(__instance, sourcePlayer.inventory, targetId, iTargetPos, amount, default);
                if (blocked is bool b && !b)
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] CanMoveItem: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Insert), new[] { typeof(Item) })]
    internal static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!PlayerSkinsPlugin.ReskinLootHooksActive || !__result) return;
            try { PlayerSkinsMod.Instance?.Plugin?.OnItemAddedToContainer(__instance, item); }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] OnItemAddedToContainer: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.RemoveFromContainer))]
    internal static class Item_RemoveFromContainer_Patch
    {
        // RemoveFromContainer -> SetParent(null) clears item.parent before postfix.
        // Capture the container in Prefix so OnItemRemoved can run (prevents original+skinned dupe).
        [HarmonyPrefix]
        private static void Prefix(Item __instance, out ItemContainer __state)
        {
            __state = __instance?.parent;
        }

        [HarmonyPostfix]
        private static void Postfix(Item __instance, ItemContainer __state)
        {
            if (!PlayerSkinsPlugin.ReskinLootHooksActive || __state?.entityOwner == null) return;
            try { PlayerSkinsMod.Instance?.Plugin?.OnItemRemovedFromContainer(__state, __instance); }
            catch (Exception ex) { Debug.LogWarning("[PlayerSkins] OnItemRemovedFromContainer: " + ex.Message); }
        }
    }
}
