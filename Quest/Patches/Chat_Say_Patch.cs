using HarmonyChat;
using HarmonyLib;
using UnityEngine;
using QPlugin = Oxide.Plugins.Quest;

namespace QuestHarmony.Patches
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

            try
            {
                if (ChatSayBridge.Dispatch(player, message))
                    return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Quest] Chat_Say_Patch: " + ex.Message);
            }
            return true;
        }
    }
}
