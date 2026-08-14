using HarmonyLib;
using UnityEngine;

namespace KaruzaVehicles.Patches
{
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Insert))]
    internal static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result) return;
            var ce = KaruzaVehiclesMod.Instance?.CustomEntities;
            if (ce == null) return;
            try { ce.OnItemAddedToContainer(__instance, item); }
            catch (System.Exception ex) { Debug.LogWarning("[KaruzaVehicles] OnItemAddedToContainer: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Remove))]
    internal static class ItemContainer_Remove_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result) return;
            var ce = KaruzaVehiclesMod.Instance?.CustomEntities;
            if (ce == null) return;
            try { ce.OnItemRemovedFromContainer(__instance, item); }
            catch (System.Exception ex) { Debug.LogWarning("[KaruzaVehicles] OnItemRemovedFromContainer: " + ex.Message); }
        }
    }
}
