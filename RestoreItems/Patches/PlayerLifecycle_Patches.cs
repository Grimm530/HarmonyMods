using HarmonyLib;
using UnityEngine;

namespace RestoreItemsHarmony.Patches
{
    /// <summary>
    /// Oxide OnPlayerDeath runs after Belt.DropActive and before base.Die when not wounding.
    /// SleepingBag.OnPlayerDeath is the nearest stable anchor after DropActive.
    /// </summary>
    [HarmonyPatch(typeof(SleepingBag), nameof(SleepingBag.OnPlayerDeath))]
    internal static class SleepingBag_OnPlayerDeath_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer player)
        {
            var plugin = RestoreItemsHarmonyMod.Plugin;
            if (plugin == null || player == null) return;
            var info = DeathHookState.LastHitInfo;
            try { plugin.DispatchOnPlayerDeath(player, info); }
            catch (System.Exception ex) { Debug.LogWarning("[RestoreItems] OnPlayerDeath: " + ex.Message); }
            finally { DeathHookState.Clear(); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    internal static class BasePlayer_Die_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, HitInfo info)
        {
            if (__instance == null || __instance.IsDead()) return;
            if (__instance.EligibleForWounding(info)) return;
            DeathHookState.LastHitInfo = info;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDied), new[] { typeof(HitInfo) })]
    internal static class BasePlayer_OnDied_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance, HitInfo info)
        {
            var plugin = RestoreItemsHarmonyMod.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.DispatchOnDied(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[RestoreItems] OnDied: " + ex.Message); }
        }
    }
}
