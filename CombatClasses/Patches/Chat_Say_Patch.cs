using System;
using HarmonyChat;
using HarmonyLib;
using UnityEngine;
using CCPlugin = Oxide.Plugins.CombatClasses;
using ChatChannel = ConVar.Chat.ChatChannel;

namespace CombatClassesHarmony.Patches
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
            if (string.IsNullOrEmpty(message)) return true;

            var player = arg.Player();
            if (player == null || !player.IsConnected) return true;

            try
            {
                // Slash commands via shared bridge (/class, /gearbox, …)
                if (message.StartsWith("/"))
                {
                    if (ChatSayBridge.Dispatch(player, message))
                        return false;
                    return true;
                }

                // BetterChat owns formatted chat (titles). Class tag is registered as a BetterChat title.
                try
                {
                    if (AppDomain.CurrentDomain.GetData("BetterChat_ApiType") != null)
                        return true;
                }
                catch { }

                // Class chat prefix (showchatclass) — only when BetterChat is not loaded
                object chatResult = CCPlugin.Dispatch_OnPlayerChat(player, message, ChatChannel.Global);
                if (chatResult != null)
                    return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CombatClasses] Chat_Say_Patch: " + ex.Message);
            }
            return true;
        }
    }
}
