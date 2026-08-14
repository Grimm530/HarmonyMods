using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace InventoryViewer.Patches
{
    [HarmonyPatch(typeof(Chat), nameof(Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string text = arg.GetString(0, string.Empty)?.Trim();
            if (string.IsNullOrEmpty(text) || (!text.StartsWith("/") && !text.StartsWith("\\")))
                return true;
            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;
            string[] parts = text.Substring(1).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return true;
            var mod = InventoryViewerMod.Instance;
            if (mod == null) return true;
            string command = parts[0];
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];
            return !mod.TryHandleChat(player, command, args);
        }
    }

    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    internal static class StorageContainer_PlayerStoppedLooting_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(StorageContainer __instance, BasePlayer player)
        {
            var plugin = InventoryViewerMod.Instance?.Plugin;
            if (plugin == null || player == null) return;
            try { plugin.OnLootEntityEnd(player, __instance); }
            catch (Exception ex) { Debug.LogWarning("[InventoryViewer] OnLootEntityEnd: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(LootableCorpse), "PlayerStoppedLooting")]
    internal static class LootableCorpse_PlayerStoppedLooting_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(LootableCorpse __instance, BasePlayer player)
        {
            var plugin = InventoryViewerMod.Instance?.Plugin;
            if (plugin == null || player == null) return;
            try { plugin.OnLootEntityEnd(player, null); }
            catch (Exception ex) { Debug.LogWarning("[InventoryViewer] OnLootEntityEnd corpse: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer))]
    internal static class Item_MoveToContainer_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Item __instance, ItemContainer newcontainer, int iTargetPos, BasePlayer sourcePlayer)
        {
            var plugin = InventoryViewerMod.Instance?.Plugin;
            if (plugin == null || sourcePlayer?.inventory == null) return true;
            try
            {
                var targetId = newcontainer?.uid ?? default;
                object blocked = plugin.CanMoveItem(__instance, sourcePlayer.inventory, targetId, iTargetPos, __instance.amount);
                if (blocked is bool b && !b)
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[InventoryViewer] CanMoveItem: " + ex.Message); }
            return true;
        }
    }
}
