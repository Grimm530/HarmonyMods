using HarmonyLib;
using System;
using UnityEngine;

namespace ShopHarmony.Patches
{
    /// <summary>
    /// Oxide CanLootEntity(player, VendingMachine) — open custom shop UI instead of vanilla loot.
    /// Patches the same entry Oxide uses (PlayerLoot.StartLootingEntity). Early-out unless the
    /// target is a VendingMachine so other mods (e.g. RaidableBases) still run.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity))]
    public static class PlayerLoot_StartLootingEntity_Shop_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity)
        {
            try
            {
                if (targetEntity is not VendingMachine vm) return true;
                var player = __instance?.baseEntity;
                if (player == null) return true;

                var plugin = ShopHarmonyMod.Instance?.Plugin;
                if (plugin == null) return true;

                var result = plugin.CanLootEntity(player, vm);
                // Oxide: non-null (typically false) blocks looting
                return result == null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] CanLootEntity: " + ex.Message);
                return true;
            }
        }
    }
}
