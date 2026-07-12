using HarmonyLib;

namespace PlatformSync.Patches
{
    /// <summary>Prefix on ConVar.Chat.say — intercept /link, /testlink, /testurl.</summary>
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    internal static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message)) return true;
            var plugin = PlatformSyncPlugin.Instance;
            if (plugin == null) return true;
            var player = Compat.GetPlayer(arg);
            if (player == null) return true;
            bool handled = plugin.OnChatCommand(player, message);
            return !handled;
        }
    }
}
