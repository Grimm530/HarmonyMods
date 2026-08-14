using System;
using HarmonyLib;
using UnityEngine;

namespace LootQoLHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.OnAttacked), new[] { typeof(HitInfo) })]
    internal static class BaseCombatEntity_OnAttacked_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance is not LootContainer) return;
            var plugin = LootQoLMod.Plugin;
            if (plugin == null) return;
            var attacker = info?.InitiatorPlayer;
            if (attacker == null) return;
            try { plugin.OnPlayerAttack(attacker, info); }
            catch (System.Exception ex) { Debug.LogWarning("[LootQoL] OnPlayerAttack: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    internal static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance is not LootContainer barrel) return;
            var plugin = LootQoLMod.Plugin;
            if (plugin == null) return;
            try { plugin.OnEntityDeath(barrel, info); }
            catch (System.Exception ex) { Debug.LogWarning("[LootQoL] OnEntityDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) })]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            if (__instance is not LootContainer loot) return;
            var plugin = LootQoLMod.Plugin;
            if (plugin == null) return;
            try { plugin.OnEntityKill(loot); }
            catch (System.Exception ex) { Debug.LogWarning("[LootQoL] OnEntityKill: " + ex.Message); }
        }
    }
}
