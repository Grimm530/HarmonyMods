using HarmonyLib;
using System;
using UnityEngine;

namespace BackpacksHarmony.Patches
{
    /// <summary>Oxide CanMoveItem — block edits for readonly backpack viewers.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer))]
    public static class Item_MoveToContainer_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, ItemContainer newcontainer, int iTargetPos, bool allowStack,
            bool ignoreStackLimit, BasePlayer sourcePlayer, bool allowSwap)
        {
            try
            {
                var plugin = BackpacksHarmonyMod.Instance?.Plugin;
                if (plugin == null || !plugin.IsSubscribed(nameof(plugin.CanMoveItem)))
                    return true;

                var inv = sourcePlayer?.inventory;
                if (inv == null) return true;

                var targetId = newcontainer?.uid ?? default;
                var amount = __instance?.amount ?? 0;
                var blocked = plugin.CanMoveItem(__instance, inv, targetId, iTargetPos, amount);
                if (blocked is bool b && !b)
                    return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] CanMoveItem: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide OnItemAction — block item actions for readonly backpack viewers.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.ServerCommand))]
    public static class Item_ServerCommand_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, string command, BasePlayer player)
        {
            try
            {
                var plugin = BackpacksHarmonyMod.Instance?.Plugin;
                if (plugin == null || !plugin.IsSubscribed(nameof(plugin.OnItemAction)))
                    return true;

                var blocked = plugin.OnItemAction(__instance, command, player);
                if (blocked is bool b && !b)
                    return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnItemAction: " + ex.Message);
            }
            return true;
        }
    }
}
