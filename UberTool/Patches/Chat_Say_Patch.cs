using System;
using System.Linq;
using HarmonyChat;
using HarmonyLib;
using UnityEngine;

namespace UberToolHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(HarmonyLib.Priority.Normal)]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message)) return true;
            var player = arg.Player();
            if (player == null || !player.IsConnected) return true;
            try
            {
                if (message.StartsWith("/") || message.StartsWith("\\"))
                {
                    string[] parts = message.Substring(1).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) return true;
                    string command = parts[0];
                    string[] argsArr = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();

                    if (ChatSayBridge.Dispatch(player, message))
                        return false;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UberTool] Chat_Say_Patch: " + ex.Message);
            }
            return true;
        }
    }
}
