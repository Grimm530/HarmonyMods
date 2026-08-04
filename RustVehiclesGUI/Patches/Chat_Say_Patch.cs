using System;
using HarmonyLib;
using UnityEngine;

namespace RustVehiclesGUIHarmony.Patches
{
    /// <summary>
    /// Routes the chat aliases from HarmonyConfig/RustVehiclesGUI.json ("Chat Commands") into
    /// the GUI open handler. Patches chat.say and chat.localsay.
    /// </summary>
    internal static class ChatCommandRouter
    {
        internal static bool TryHandle(ConsoleSystem.Arg arg)
        {
            if (arg == null) return false;

            string text = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                try
                {
                    if (arg.Args != null && arg.Args.Length > 0)
                        text = arg.Args[0].ToString()?.Trim();
                }
                catch { }
            }
            if (string.IsNullOrEmpty(text)) return false;
            if (!text.StartsWith("/") && !text.StartsWith("\\")) return false;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsConnected) return false;

            string[] parts = text.Substring(1).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            var mod = RustVehiclesGUIHarmonyMod.Instance;
            if (mod == null) return false;

            string command = parts[0];
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++)
                args[i - 1] = parts[i];

            return mod.TryHandleChat(player, command, args);
        }
    }

    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            try
            {
                if (ChatCommandRouter.TryHandle(arg))
                    return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehiclesGUI] Chat.say: " + ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.localsay))]
    internal static class Chat_LocalSay_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            try
            {
                if (ChatCommandRouter.TryHandle(arg))
                    return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RustVehiclesGUI] Chat.localsay: " + ex.Message);
            }
            return true;
        }
    }
}
