using System;
using ConVar;
using HarmonyLib;

namespace RestoreItemsHarmony.Patches
{
    [HarmonyPatch(typeof(Chat), nameof(Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string text = arg.GetString(0, string.Empty)?.Trim();
            if (string.IsNullOrEmpty(text)) return true;

            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;

            var mod = RestoreItemsHarmonyMod.Instance;
            if (mod == null) return true;
            return !mod.TryHandleChat(player, text);
        }
    }
}
