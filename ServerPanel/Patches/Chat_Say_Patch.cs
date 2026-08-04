using HarmonyLib;
using UnityEngine;

namespace ServerPanelHarmony.Patches
{
    /// <summary>
    /// Category commands (from HarmonyData/ServerPanel/Categories.json) and pop-up commands are chat
    /// commands under Oxide. Route them here and swallow the chat line when one matches.
    /// </summary>
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || !message.StartsWith("/")) return true;

            var mod = ServerPanelHarmonyMod.Instance;
            if (mod == null) return true;

            var player = arg.Player();
            if (player == null) return true;

            return !mod.OnChatCommand(player, message);
        }
    }
}
