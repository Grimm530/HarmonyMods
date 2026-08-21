using ConVar;
using HarmonyLib;

namespace LootQoLHarmony.Patches
{
    /// <summary>
    /// Routes /sortbutton (and configured aliases). Early-out unless the message is a chat command.
    /// </summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || (message[0] != '/' && message[0] != '\\'))
                return true;
            var mod = LootQoLMod.Instance;
            if (mod == null) return true;
            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;
            return !mod.TryHandleChat(player, message);
        }
    }
}
