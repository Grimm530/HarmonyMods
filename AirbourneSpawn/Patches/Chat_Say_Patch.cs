using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    [HarmonyPatch(typeof(Chat), nameof(Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string text = arg.GetString(0, string.Empty)?.Trim();
            if (string.IsNullOrEmpty(text) || (!text.StartsWith("/") && !text.StartsWith("\\")))
                return true;

            var plugin = Hooks.Plugin;
            if (plugin == null) return true;

            BasePlayer player = arg.Connection?.player as BasePlayer ?? ArgEx.Player(arg);
            if (player == null) return true;

            try
            {
                if (plugin.TryBlockMountedCommand(player))
                    return false;
            }
            catch (Exception ex) { Hooks.Warn("OnPlayerCommand", ex); }
            return true;
        }
    }
}
