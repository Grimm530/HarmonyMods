using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace Trade.Patches
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
            var mod = TradeMod.Instance;
            if (mod == null) return true;
            string command = parts[0];
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];
            return !mod.TryHandleChat(player, command, args);
        }
    }

    [HarmonyPatch(typeof(BaseEntity.RPC_Server.IsVisible), nameof(BaseEntity.RPC_Server.IsVisible.Test))]
    internal static class EntityVisibility_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(uint id, string debugName, BaseEntity ent, BasePlayer player, float maximumDistance, ref bool __result)
        {
            var plugin = TradeMod.Instance?.Plugin;
            if (plugin == null || ent == null) return true;
            try
            {
                object hook = plugin.OnEntityVisibilityCheck(ent, player, id, debugName, maximumDistance);
                if (hook is bool b)
                {
                    __result = b;
                    return false;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Trade] OnEntityVisibilityCheck: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(ShopFront), nameof(ShopFront.CompleteTrade))]
    internal static class ShopFront_CompleteTrade_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ShopFront __instance)
        {
            var plugin = TradeMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnShopCompleteTrade(__instance); }
            catch (Exception ex) { Debug.LogWarning("[Trade] OnShopCompleteTrade: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    internal static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerLoot __instance)
        {
            var plugin = TradeMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerLootEnd(__instance); }
            catch (Exception ex) { Debug.LogWarning("[Trade] OnPlayerLootEnd: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer))]
    internal static class Item_MoveToContainer_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Item __instance, ItemContainer newcontainer, BasePlayer sourcePlayer)
        {
            var plugin = TradeMod.Instance?.Plugin;
            if (plugin == null || sourcePlayer?.inventory == null) return true;
            try
            {
                var targetId = newcontainer?.uid ?? default;
                object blocked = plugin.CanMoveItem(__instance, sourcePlayer.inventory, targetId);
                if (blocked is bool b && !b)
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[Trade] CanMoveItem: " + ex.Message); }
            return true;
        }
    }
}
