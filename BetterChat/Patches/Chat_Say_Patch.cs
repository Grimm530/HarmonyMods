using ConVar;
using HarmonyChat;
using HarmonyLib;
using UnityEngine;

namespace BetterChatHarmony.Patches
{
    /// <summary>
    /// Many Harmony mods prefix Chat.say and return false, which skips sayImpl entirely.
    /// Format and send from here (Priority.First) so titles are not lost.
    /// </summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.say))]
    [HarmonyPriority(Priority.First)]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message)) return true;

            var player = arg.Player() ?? arg.Connection?.player as BasePlayer;
            if (player == null) return true;

            if (ChatSayBridge.Dispatch(player, message))
                return false;

            if (message[0] == '/' || message[0] == '\\')
                return true;

            var mod = BetterChatMod.Instance;
            if (mod == null) return true;

            return mod.HandleSayImpl(Chat.globalchat ? Chat.ChatChannel.Global : Chat.ChatChannel.Local, arg);
        }
    }
}
