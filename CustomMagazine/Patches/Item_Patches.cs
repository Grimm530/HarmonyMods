using HarmonyLib;
using UnityEngine;

namespace CustomMagazineHarmony.Patches
{
    [HarmonyPatch(typeof(Item), nameof(Item.CanStack))]
    internal static class Item_CanStack_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Item __instance, Item item, ref bool __result)
        {
            var plugin = CustomMagazineMod.Plugin;
            if (plugin == null || __instance == null || item == null) return true;
            if (__instance.info == null || item.info == null) return true;
            if (__instance.info.itemid == item.info.itemid && __instance.skin != item.skin)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.SplitItem))]
    internal static class Item_SplitItem_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Item __instance, int split_Amount, ref Item __result)
        {
            var plugin = CustomMagazineMod.Plugin;
            if (plugin == null || __instance == null) return true;
            try
            {
                if (plugin.TryCustomSplit(__instance, split_Amount, out Item created) && created != null)
                {
                    __result = created;
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CustomMagazine] OnItemSplit: " + ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DroppedItem), nameof(DroppedItem.OnDroppedOn))]
    internal static class DroppedItem_OnDroppedOn_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(DroppedItem __instance, DroppedItem di)
        {
            var plugin = CustomMagazineMod.Plugin;
            if (plugin == null || __instance?.item == null || di?.item == null) return true;
            if (__instance.item.info != null && di.item.info != null
                && __instance.item.info.itemid == di.item.info.itemid
                && __instance.item.skin != di.item.skin)
            {
                return false;
            }
            return true;
        }
    }
}
