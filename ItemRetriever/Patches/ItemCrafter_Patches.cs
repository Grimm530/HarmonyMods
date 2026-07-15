using HarmonyLib;

namespace ItemRetrieverHarmony
{
    /// <summary>
    /// Oxide OnIngredientsCollect -> ItemCrafter.CollectIngredients
    /// Oxide: if hook returns non-null, skip vanilla collection.
    /// </summary>
    [HarmonyPatch(typeof(ItemCrafter), "CollectIngredients",
        new[] { typeof(ItemBlueprint), typeof(ItemCraftTask), typeof(int), typeof(BasePlayer), typeof(bool) })]
    internal static class ItemCrafter_CollectIngredients_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ItemCrafter __instance, ItemBlueprint bp, ItemCraftTask task, int amount,
            BasePlayer player, bool takeBroken)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                // ItemRetriever signature omits takeBroken; Oxide still matches.
                var result = plugin.OnIngredientsCollect(__instance, bp, task, amount, player);
                if (result != null)
                    return false; // False object — skip vanilla
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnIngredientsCollect: " + ex.Message);
            }

            return true;
        }
    }

    /// <summary>
    /// Oxide CanCraft(ItemCrafter, ItemBlueprint, int, bool) -> ItemCrafter.CanCraft(ItemBlueprint, int, bool)
    /// Prefix: if returns false object, skip craft (return false); if True, allow (return true skip vanilla check).
    /// if null, continue vanilla.
    /// </summary>
    [HarmonyPatch(typeof(ItemCrafter), nameof(ItemCrafter.CanCraft), typeof(ItemBlueprint), typeof(int), typeof(bool))]
    internal static class ItemCrafter_CanCraft_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ItemCrafter __instance, ItemBlueprint bp, int amount, bool free, ref bool __result)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                var result = plugin.CanCraft(__instance, bp, amount, free);
                if (result is bool b)
                {
                    __result = b;
                    return false;
                }
                // ObjectCache True/False are boxed bools via True/False static objects — also object identity
                if (ReferenceEquals(result, true) || (result != null && result.Equals(true)))
                {
                    __result = true;
                    return false;
                }
                if (ReferenceEquals(result, false) || (result != null && result.Equals(false)))
                {
                    __result = false;
                    return false;
                }
                // null => continue vanilla
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] CanCraft: " + ex.Message);
            }

            return true;
        }
    }
}
