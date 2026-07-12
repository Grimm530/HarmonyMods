using HarmonyLib;
using UnityEngine;

namespace RustRewardsHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message)) return true;
            var mod = RustRewardsHarmonyMod.Instance;
            if (mod == null) return true;
            var player = arg.Player();
            if (player == null) return true;
            bool handled = mod.OnChatCommand(player, message);
            return !handled;
        }
    }
}
