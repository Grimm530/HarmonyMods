// Routes chat commands starting with / to TruePVEMod.OnChatCommand.
using HarmonyLib;
using UnityEngine;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Normal)]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || !message.StartsWith("/")) return true;

            var player = arg.Player();
            if (player == null || !player.IsConnected) return true;

            var mod = TruePVEMod.Instance;
            if (mod == null) return true;

            try
            {
                if (mod.OnChatCommand(player, message))
                    return false; // consumed
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] Chat_Say_Patch: " + ex.Message); }
            return true;
        }
    }
}
