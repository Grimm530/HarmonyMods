using System;
using HarmonyLib;
using UnityEngine;

namespace LootQoLHarmony.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    internal static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || targetEntity == null) return;
            var plugin = LootQoLMod.Plugin;
            if (plugin == null) return;
            BasePlayer player = __instance != null ? __instance.baseEntity : null;
            if (player == null) return;
            try { plugin.OnLootEntity(player, targetEntity); }
            catch (Exception ex) { Debug.LogWarning("[LootQoL] OnLootEntity: " + ex.Message); }
        }
    }

    /// <summary>
    /// Oxide OnLootEntityEnd fires from PlayerStoppedLooting, which PlayerLoot.Clear
    /// invokes for every loot close (ESC, walk-away Check, EndLooting, swapping targets).
    /// Patching BasePlayer.EndLooting misses the walk-away / Check path, leaving Take all on screen.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    internal static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerLoot __instance, out object __state)
        {
            if (__instance == null || !__instance.IsLooting())
            {
                __state = false;
                return;
            }
            __state = __instance.entitySource;
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerLoot __instance, object __state)
        {
            if (__state is bool) return;
            var plugin = LootQoLMod.Plugin;
            var player = __instance?.baseEntity;
            if (plugin == null || player == null) return;
            try { plugin.OnLootEntityEnd(player, __state as BaseEntity); }
            catch (Exception ex) { Debug.LogWarning("[LootQoL] OnLootEntityEnd: " + ex.Message); }
        }
    }
}
