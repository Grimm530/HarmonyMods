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
    /// Destroy overlay CUI in the prefix even when Clear is a no-op, so a stuck Sort button
    /// (Overlay parent) is still removed. LootBouncer only runs when a loot session was active.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    internal static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerLoot __instance)
        {
            var plugin = LootQoLMod.Plugin;
            var player = __instance?.baseEntity;
            if (plugin == null || player == null) return;
            try
            {
                if (__instance.IsLooting())
                    plugin.OnLootEntityEnd(player, __instance.entitySource);
                else
                    plugin.DestroyLootOverlayUi(player);
            }
            catch (Exception ex) { Debug.LogWarning("[LootQoL] OnLootEntityEnd: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    internal static class StorageContainer_PlayerStoppedLooting_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer player)
        {
            if (player == null) return;
            try { LootQoLMod.Plugin?.DestroyLootOverlayUi(player); }
            catch (Exception ex) { Debug.LogWarning("[LootQoL] PlayerStoppedLooting: " + ex.Message); }
        }
    }
}
