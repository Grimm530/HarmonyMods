using HarmonyLib;
using UnityEngine;

namespace PlayerSkinsHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
    internal static class BasePlayer_Die_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance, HitInfo info)
        {
            if (!PlayerSkinsPlugin.ReskinLootHooksActive) return;
            try { PlayerSkinsMod.Instance?.Plugin?.OnPlayerDeath(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[PlayerSkins] OnPlayerDeath: " + ex.Message); }
        }
    }

    /// <summary>
    /// Oxide OnActiveItemChanged only fires when the active item actually changes.
    /// UpdateActiveItem early-returns on same ItemId, but Harmony postfix still runs —
    /// so we must gate on a real change or the shop UI is destroyed while the mouse lock stays.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.UpdateActiveItem), typeof(ItemId))]
    internal static class BasePlayer_UpdateActiveItem_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, ItemId itemID, ref bool __state)
        {
            __state = __instance != null && __instance.svActiveItemID != itemID;
        }

        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance, ItemId itemID, bool __state)
        {
            if (!__state) return;
            var plugin = PlayerSkinsMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try
            {
                Item newItem = __instance.GetActiveItem();
                plugin.OnActiveItemChanged(__instance, null, newItem);
            }
            catch (System.Exception ex) { Debug.LogWarning("[PlayerSkins] OnActiveItemChanged: " + ex.Message); }
        }
    }
}
