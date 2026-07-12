using HarmonyLib;
using UnityEngine;

namespace Backpacks.Patches
{
    /// <summary>
    /// When the game calls PlayerStoppedLooting on our backpack entity (coffin with BackpackStorageMarker),
    /// destroy the page buttons UI. Same as Oxide Backpacks: entity receives SendMessage("PlayerStoppedLooting", player) from PlayerLoot.Clear().
    /// </summary>
    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    public static class Patch_StorageContainer_PlayerStoppedLooting
    {
        [HarmonyPostfix]
        public static void Postfix(StorageContainer __instance, BasePlayer player)
        {
            if (player == null || __instance == null) return;
            var marker = __instance.GetComponent<BackpackStorageMarker>();
            if (marker == null) return;

            // Full cleanup here (save, kill entities, remove state, destroy UI). This is the reliable path
            // because we have the player and OwnerId; the Clear() Prefix may miss if GetPlayerFromPlayerLoot fails.
            BackpacksMod.Instance?.OnBackpackEntityStoppedLooting(player, marker.OwnerId);
        }
    }
}
