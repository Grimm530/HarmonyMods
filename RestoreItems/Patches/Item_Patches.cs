using HarmonyLib;
using UnityEngine;

namespace RestoreItemsHarmony.Patches
{
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Insert), new[] { typeof(Item) })]
    internal static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result || __instance == null || item == null) return;
            var plugin = RestoreItemsHarmonyMod.Plugin;
            if (plugin == null) return;
            try { plugin.DispatchOnItemAddedToContainer(__instance, item); }
            catch (System.Exception ex) { Debug.LogWarning("[RestoreItems] OnItemAddedToContainer: " + ex.Message); }
        }
    }

    /// <summary>
    /// Oxide OnItemStacked fires after stack merge in Item.MoveToContainer (same container).
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.MigrateItemOwnership), new[] { typeof(Item), typeof(int) })]
    internal static class Item_MigrateItemOwnership_StackPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Item __instance, Item targetItem, int amount)
        {
            if (__instance == null || targetItem == null || amount <= 0) return;
            if (targetItem.parent == null || __instance.parent == null) return;
            if (!ReferenceEquals(targetItem.parent, __instance.parent)) return;

            var plugin = RestoreItemsHarmonyMod.Plugin;
            if (plugin == null) return;
            try { plugin.DispatchOnItemStacked(targetItem, __instance, targetItem.parent, amount); }
            catch (System.Exception ex) { Debug.LogWarning("[RestoreItems] OnItemStacked: " + ex.Message); }
        }
    }
}
