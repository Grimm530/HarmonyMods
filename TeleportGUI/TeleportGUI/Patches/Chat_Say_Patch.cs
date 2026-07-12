using System;
using ConVar;
using HarmonyLib;

namespace TeleportGUI.Patches
{
    /// <summary>Intercepts chat so /tp, /home, /warp, /tpback, /death are handled by the mod.</summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            var msg = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(msg)) return true;
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;

            var mod = TeleportGUIMod.Instance;
            if (mod == null) return true;

            if (!msg.StartsWith("/") && !msg.StartsWith("\\")) return true;
            var rest = msg.Substring(1).Trim();
            if (rest.Length == 0) return true;

            var parts = rest.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            var args = parts.Length > 1 ? new string[parts.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < parts.Length; i++) args[i - 1] = parts[i];

            if (mod.RunCommand(player, cmd, args))
                return false;
            return true;
        }
    }
}
