using System.Collections.Generic;
using HarmonyLib;

namespace ItemRetrieverHarmony
{
    /// <summary>
    /// Oxide OnInventoryAmmoItemFind(PlayerInventory, ItemDefinition) — used by Chainsaw/FlameThrower fuel lookup.
    /// Those call FindItemByItemName after the hook; we also patch FindItemByItemID path is separate.
    /// Patch the call sites by Prefix on FindItemByItemName when used for fuel — fragile.
    /// Better: patch Chainsaw/FlameThrower methods if they exist, OR expose via FindItemByItemID already covered
    /// when itemid is known. Oxide hooks specifically on fuelType ItemDefinition.
    ///
    /// Patch PlayerInventory with a helper invoked from known callers via Harmony on:
    /// - methods that Oxide hooked: Chainsaw GetFuel / FlameThrower equivalent
    /// </summary>
    internal static class InventoryAmmoItemFind_ByDefinition
    {
        internal static Item TryFind(PlayerInventory inventory, ItemDefinition itemDefinition)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null || (object)itemDefinition == null)
                return null;

            try
            {
                return plugin.OnInventoryAmmoItemFind(inventory, itemDefinition);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryAmmoItemFind(ItemDefinition): " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>Try common fuel-consuming entities if present in this Rust build.</summary>
    [HarmonyPatch]
    internal static class Chainsaw_Fuel_Patch
    {
        static bool Prepare() => TargetMethod() != null;

        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Chainsaw");
            if (t == null) return null;
            foreach (var name in new[] { "GetFuel", "FindFuel", "GetAmmo" })
            {
                var m = AccessTools.Method(t, name);
                if (m != null && m.ReturnType == typeof(Item))
                    return m;
            }
            return null;
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance, ref Item __result)
        {
            try
            {
                var held = __instance as HeldEntity;
                var owner = held?.GetOwnerPlayer();
                if (owner?.inventory == null) return true;

                var fuelTypeField = AccessTools.Field(__instance.GetType(), "fuelType");
                var fuelType = fuelTypeField?.GetValue(__instance) as ItemDefinition;
                var found = InventoryAmmoItemFind_ByDefinition.TryFind(owner.inventory, fuelType);
                if (found != null)
                {
                    __result = found;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class FlameThrower_Fuel_Patch
    {
        static bool Prepare() => TargetMethod() != null;

        static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("FlameThrower");
            if (t == null) return null;
            foreach (var name in new[] { "GetFuel", "FindFuel", "GetAmmo" })
            {
                var m = AccessTools.Method(t, name);
                if (m != null && m.ReturnType == typeof(Item))
                    return m;
            }
            return null;
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance, ref Item __result)
        {
            try
            {
                var held = __instance as HeldEntity;
                var owner = held?.GetOwnerPlayer();
                if (owner?.inventory == null) return true;

                var fuelTypeField = AccessTools.Field(__instance.GetType(), "fuelType");
                var fuelType = fuelTypeField?.GetValue(__instance) as ItemDefinition;
                var found = InventoryAmmoItemFind_ByDefinition.TryFind(owner.inventory, fuelType);
                if (found != null)
                {
                    __result = found;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
