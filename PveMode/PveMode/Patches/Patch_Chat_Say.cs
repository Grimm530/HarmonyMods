// Routes chat commands starting with / to PveModeMod.OnChatCommand (handles /EventsTime).
using HarmonyLib;
using UnityEngine;

namespace PveModeHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Patch_Chat_Say
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || !message.StartsWith("/")) return true;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsConnected) return true;

            PveModeMod mod = PveModeMod.Instance;
            if (mod == null) return true;

            try
            {
                if (mod.OnChatCommand(player, message)) return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[PveMode] Chat_Say_Patch: " + ex.Message);
            }
            return true;
        }
    }
}
