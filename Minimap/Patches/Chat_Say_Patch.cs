using HarmonyChat;
using HarmonyLib;

namespace MinimapHarmony.Patches
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
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;

            bool handled = ChatSayBridge.Dispatch(player, message);
            return !handled;
        }
    }
}
