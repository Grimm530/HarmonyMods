using HarmonyLib;
using UnityEngine;
using MSPlugin = Oxide.Plugins.MovementSpeed;

namespace MovementSpeedHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            try { MovementSpeedMod.Plugin?.DispatchConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[MovementSpeed] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            try { MovementSpeedMod.Plugin?.DispatchDisconnected(__instance, ""); }
            catch (System.Exception ex) { Debug.LogWarning("[MovementSpeed] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message)) return true;
            var mod = MovementSpeedMod.Instance;
            if (mod == null) return true;
            var player = arg.Player();
            if (player == null) return true;
            return !mod.OnChatCommand(player, message);
        }
    }
}
