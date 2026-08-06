using System.Collections.Generic;
using HarmonyLib;

namespace ItemRetrieverHarmony
{
    /// <summary>Oxide OnInventoryItemsCount -> PlayerInventory.GetAmount(int, bool, bool)</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.GetAmount), typeof(int), typeof(bool), typeof(bool))]
    internal static class PlayerInventory_GetAmount_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerInventory __instance, int itemid, bool includeBackpack, bool redirectAllowed, ref int __result)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null || itemid == 0)
                return true;

            try
            {
                var result = plugin.OnInventoryItemsCount(__instance, itemid);
                if (result is int count)
                {
                    __result = count;
                    return false;
                }
                // ObjectCache returns boxed int as object — also accept via Convert
                if (result != null)
                {
                    __result = System.Convert.ToInt32(result);
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryItemsCount: " + ex.Message);
            }

            return true;
        }
    }

    /// <summary>Oxide OnInventoryItemsTake -> PlayerInventory.Take</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.Take), typeof(List<Item>), typeof(int), typeof(int))]
    internal static class PlayerInventory_Take_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerInventory __instance, List<Item> collect, int itemid, int amount, ref int __result)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                var result = plugin.OnInventoryItemsTake(__instance, collect, itemid, amount);
                if (result != null)
                {
                    __result = System.Convert.ToInt32(result);
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryItemsTake: " + ex.Message);
            }

            return true;
        }
    }

    /// <summary>Oxide OnInventoryItemsFind -> PlayerInventory.FindItemsByItemID</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.FindItemsByItemID), typeof(List<Item>), typeof(int))]
    internal static class PlayerInventory_FindItemsByItemID_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerInventory __instance, List<Item> list, int id)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                // Oxide hook args: (this, id, list) — ItemRetriever signature (inventory, itemId, collect)
                var result = plugin.OnInventoryItemsFind(__instance, id, list);
                if (result != null)
                    return false; // False object skips vanilla
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryItemsFind: " + ex.Message);
            }

            return true;
        }
    }

    /// <summary>Oxide OnInventoryItemFind -> PlayerInventory.FindItemByItemID(int)</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.FindItemByItemID), typeof(int))]
    internal static class PlayerInventory_FindItemByItemID_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerInventory __instance, int id, ref Item __result)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                var result = plugin.OnInventoryItemFind(__instance, id);
                if (result is Item item)
                {
                    __result = item;
                    return false;
                }
                // Hook always returns FirstOrDefault (may be null) as object — Oxide replaces if is Item.
                // ItemRetriever returns Item or null directly as object from FirstOrDefault.
                if (result == null)
                {
                    // Still replace with null? Oxide only replaces if is Item. Fall through for null.
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryItemFind: " + ex.Message);
            }

            return true;
        }
    }

    /// <summary>Oxide OnInventoryAmmoFind -> PlayerInventory.FindAmmo(List, AmmoTypes)</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.FindAmmo), typeof(List<Item>), typeof(Rust.AmmoTypes))]
    internal static class PlayerInventory_FindAmmo_List_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerInventory __instance, List<Item> list, Rust.AmmoTypes ammoType)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                var result = plugin.OnInventoryAmmoFind(__instance, list, ammoType);
                if (result != null)
                    return false;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryAmmoFind: " + ex.Message);
            }

            return true;
        }
    }

    /// <summary>Oxide OnInventoryAmmoItemFind -> PlayerInventory.FindAmmo(AmmoTypes)</summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.FindAmmo), typeof(Rust.AmmoTypes))]
    internal static class PlayerInventory_FindAmmo_Single_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerInventory __instance, Rust.AmmoTypes ammoType, ref Item __result)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                var result = plugin.OnInventoryAmmoItemFind(__instance, ammoType);
                if (result != null)
                {
                    __result = result;
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryAmmoItemFind(AmmoTypes): " + ex.Message);
            }

            return true;
        }
    }
}
