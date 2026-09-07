using System;
using ConVar;
using HarmonyLib;

namespace AdminTime.Patches
{
    /// <summary>Intercepts chat so /mytime, /myweather, /myweather.clear, /storm are handled and not shown in chat.</summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string msg = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(msg)) return true;
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;

            var mod = AdminTimeMod.Instance;
            if (mod == null) return true;

            if (!msg.StartsWith("/") && !msg.StartsWith("\\")) return true;
            string rest = msg.Substring(1).Trim();
            if (rest.Length == 0) return true;

            string[] parts = rest.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            string[] args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++) args[i - 1] = parts[i];

            if (mod.RunChatCommand(player, cmd, args))
                return false;
            return true;
        }
    }
}
