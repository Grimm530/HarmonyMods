using HarmonyLib;
using UnityEngine;

namespace BGrade.Patches
{
    /// <summary>Oxide OnEntityBuilt — Planner.DoBuild(Construction.Target, Construction) returns BaseEntity.</summary>
    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    internal static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Planner __instance, BaseEntity __result)
        {
            var plugin = BGradeMod.Instance?.Plugin;
            if (plugin == null || __instance == null || __result == null) return;
            try { plugin.OnEntityBuilt(__instance, __result.gameObject); }
            catch (System.Exception ex) { Debug.LogWarning("[BGrade] OnEntityBuilt: " + ex.Message); }
        }
    }

    /// <summary>Oxide OnPayForPlacement — non-null skip (nores perm skips twig cost).</summary>
    [HarmonyPatch(typeof(Planner), nameof(Planner.PayForPlacement))]
    internal static class Planner_PayForPlacement_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Planner __instance, BasePlayer player, Construction component)
        {
            var plugin = BGradeMod.Instance?.Plugin;
            if (plugin == null || __instance == null || player == null) return true;
            try
            {
                if (plugin.OnPayForPlacement(player, __instance, component) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[BGrade] OnPayForPlacement: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), typeof(HitInfo))]
    internal static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            var plugin = BGradeMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            var block = __instance as BuildingBlock;
            if (block == null) return;
            try { plugin.OnEntityDeath(block, info); }
            catch (System.Exception ex) { Debug.LogWarning("[BGrade] OnEntityDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = BGradeMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[BGrade] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.DoAutomatedSave), typeof(bool))]
    internal static class ServerSave_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            var plugin = BGradeMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[BGrade] OnServerSave: " + ex.Message); }
        }
    }
}
