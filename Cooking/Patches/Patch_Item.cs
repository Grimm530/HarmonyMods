using System;
using HarmonyLib;
using UnityEngine;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    /// <summary>Oxide OnItemAction → Item.ServerCommand. Non-null return skips the original (unwrap bag / consume cooldown).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.ServerCommand))]
    public static class Item_ServerCommand_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, string command, BasePlayer player)
        {
            try
            {
                var hook = CookingPlugin.Dispatch_OnItemAction(__instance, command, player);
                if (hook != null) return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] OnItemAction: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnItemSplit → Item.SplitItem. Returning an Item replaces vanilla split (keeps name/skin/spoil).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.SplitItem))]
    public static class Item_SplitItem_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, int split_Amount, ref Item __result)
        {
            try
            {
                var hook = CookingPlugin.Dispatch_OnItemSplit(__instance, split_Amount);
                if (hook is Item splitItem)
                {
                    __result = splitItem;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] OnItemSplit: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnPlayerAddModifiers → ItemModConsume.DoAction postfix (reset vanilla calories on cooking meals/ingredients, apply buffs).</summary>
    [HarmonyPatch(typeof(ItemModConsume), nameof(ItemModConsume.DoAction))]
    public static class ItemModConsume_DoAction_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Item item, BasePlayer player)
        {
            if (item == null || player == null) return;
            try
            {
                var consumable = item.info != null ? item.info.GetComponent<ItemModConsumable>() : null;
                CookingPlugin.Dispatch_OnPlayerAddModifiers(player, item, consumable);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cooking] OnPlayerAddModifiers: " + ex.Message);
            }
        }
    }
}
