using HarmonyLib;
using UnityEngine;

namespace ZombieHorde.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            BasePlayer player = Compat.GetPlayer(arg);
            if (player == null) return true;
            string message = arg.GetString(0, string.Empty);
            if (string.IsNullOrEmpty(message)) return true;
            var plugin = ZombieHordePlugin.Instance;
            if (plugin != null && plugin.TryHandleChatCommand(player, message))
                return false;
            return true;
        }
    }
}
