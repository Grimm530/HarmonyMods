using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace SortButton.Patches
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
            var mod = SortButtonMod.Instance;
            if (mod == null) return true;
            string command = parts[0];
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];
            return !mod.TryHandleChat(player, command, args);
        }
    }

    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;
            string marker = a[0].ToString();
            if (!string.Equals(marker, "SORTBUTTON", StringComparison.OrdinalIgnoreCase))
                return true;
            var player = args.Connection?.player as BasePlayer ?? args.Player();
            if (player == null || player.IsDestroyed || !player.IsConnected) return false;
            string action = a.Length > 1 ? a[1].ToString() : string.Empty;
            try { SortButtonMod.Instance?.HandleCui(player, action); }
            catch (Exception ex) { Debug.LogWarning("[SortButton] cui.endtest: " + ex); }
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity))]
    internal static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerLoot __instance, BaseEntity targetEntity)
        {
            var plugin = SortButtonMod.Instance?.Plugin;
            if (plugin == null || __instance == null || targetEntity == null) return;
            var player = __instance.GetComponent<BasePlayer>();
            if (player == null) return;
            try { plugin.OnLootEntity(player, targetEntity); }
            catch (Exception ex) { Debug.LogWarning("[SortButton] OnLootEntity: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    internal static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerLoot __instance)
        {
            var plugin = SortButtonMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            var player = __instance.baseEntity;
            if (player == null) return;
            try { plugin.OnPlayerLootEnd(player); }
            catch (Exception ex) { Debug.LogWarning("[SortButton] OnPlayerLootEnd: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    internal static class StorageContainer_PlayerStoppedLooting_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(StorageContainer __instance, BasePlayer player)
        {
            var plugin = SortButtonMod.Instance?.Plugin;
            if (plugin == null || player == null) return;
            try { plugin.OnLootEntityEnd(player); }
            catch (Exception ex) { Debug.LogWarning("[SortButton] OnLootEntityEnd: " + ex.Message); }
        }
    }
}
