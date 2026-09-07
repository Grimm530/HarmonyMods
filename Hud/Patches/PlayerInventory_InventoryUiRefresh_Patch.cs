using HarmonyLib;
using UnityEngine;
using HPlugin = Harmony.Plugins.Hud;

namespace HudHarmony.Patches
{
    /// <summary>
    /// Attach Inventory-parent HUD once the client has a real inventory sync.
    /// Early connect AddUI can miss the Inventory panel; item moves also sync Main,
    /// so this must run once per wake — not on every drag (that reloads CUI images
    /// and re-selects buttons, which eats hotbar keys 1-6).
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.SendUpdatedInventoryInternal))]
    public static class PlayerInventory_InventoryUiRefresh_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerInventory __instance, PlayerInventory.Type type)
        {
            if (type != PlayerInventory.Type.Main)
                return;

            var player = __instance?.baseEntity;
            if (player == null || player.IsNpc || !player.IsConnected || player.IsSleeping())
                return;
            if (player.IsReceivingSnapshot)
                return;

            var userId = player.userID;
            if (InventoryLayerUiBridge.IsReady(userId))
                return;
            if (!InventoryLayerUiBridge.HasBeenConnectedLongEnough(player))
                return;

            InventoryLayerUiBridge.MarkReady(userId);

            try
            {
                HPlugin.Dispatch_OnInventoryLayerRefresh(player);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Hud] Inventory UI refresh: " + ex.Message);
            }
        }
    }
}
