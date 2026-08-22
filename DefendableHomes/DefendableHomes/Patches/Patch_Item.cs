using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    /// <summary>Oxide CanStackItem — keep custom flare skins from stacking with vanilla flares.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.CanStack))]
    public static class Patch_Item_CanStack
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, Item item, ref bool __result)
        {
            object result = DHPlugin.Dispatch_CanStackItem(__instance, item);
            if (result is bool allow)
            {
                __result = allow;
                return false;
            }
            return true;
        }
    }

    /// <summary>Oxide OnItemSplit — preserve flare name/skin when splitting stacks.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.SplitItem))]
    public static class Patch_Item_SplitItem
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, int split_Amount, ref Item __result)
        {
            Item created = DHPlugin.Dispatch_OnItemSplit(__instance, split_Amount);
            if (created != null)
            {
                __result = created;
                return false;
            }
            return true;
        }
    }

    /// <summary>Oxide CanCombineDroppedItem — block combining flares of different skins. Non-null/true blocks.</summary>
    [HarmonyPatch(typeof(DroppedItem), nameof(DroppedItem.OnDroppedOn))]
    public static class Patch_DroppedItem_OnDroppedOn
    {
        [HarmonyPrefix]
        public static bool Prefix(DroppedItem __instance, DroppedItem di)
        {
            object result = DHPlugin.Dispatch_CanCombineDroppedItem(__instance, di);
            return result == null;
        }
    }
}
