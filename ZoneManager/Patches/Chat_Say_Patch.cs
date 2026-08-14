using System;
using HarmonyLib;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string text = arg.GetString(0, string.Empty)?.Trim();
            if (string.IsNullOrEmpty(text)) return true;

            BasePlayer player = arg.Connection?.player as BasePlayer ?? arg.Player();
            if (player == null) return true;

            try
            {
                if (text.StartsWith("/") || text.StartsWith("\\"))
                {
                    string[] parts = text.Substring(1).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) return true;

                    var mod = ZoneManagerMod.Instance;
                    if (mod == null) return true;

                    string command = parts[0];
                    string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
                    for (int i = 1; i < parts.Length; i++)
                        args[i - 1] = parts[i];

                    if (mod.TryHandleChat(player, command, args))
                        return false;
                    return true;
                }

                object blocked = ZM.Dispatch_OnPlayerChat(player, text, ConVar.Chat.ChatChannel.Global);
                if (blocked != null)
                    return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZoneManager] Chat_Say_Patch: " + ex.Message);
            }
            return true;
        }
    }
}
