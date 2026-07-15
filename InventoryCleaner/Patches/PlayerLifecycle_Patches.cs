using HarmonyLib;
using UnityEngine;

namespace InventoryCleaner.Patches
{
    /// <summary>
    /// Oxide OnPlayerDeath ran inside BasePlayer.Die after the wound check and before base.Die.
    /// Prefix + EligibleForWounding mirrors that: strip only when the player will actually die
    /// (not go wounded), so items are gone before corpse loot.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    internal static class BasePlayer_Die_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, HitInfo info)
        {
            var service = InventoryCleanerMod.Service;
            if (service == null || __instance == null || __instance.IsDead()) return;
            // Do not call WoundInsteadOfDying — it has side effects (BecomeWounded).
            if (__instance.EligibleForWounding(info)) return;
            try { service.OnPlayerDeath(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[InventoryCleaner] OnPlayerDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var service = InventoryCleanerMod.Service;
            if (service == null || __instance == null) return;
            try { service.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[InventoryCleaner] OnPlayerDisconnected: " + ex.Message); }
        }
    }
}
