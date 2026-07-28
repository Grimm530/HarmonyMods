using HarmonyLib;

namespace PersonalNPCHarmony.Patches
{
    /// <summary>
    /// Chat entry point for /pnpc, /bw and /botwheel. The Frankenstein unlock gate (Oxide
    /// OnPlayerCommand in PersonalNPCHelper) is applied inside OnChatCommand before the spawn
    /// command reaches PersonalNPC.
    /// </summary>
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;

            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || message[0] != '/') return true;

            var mod = PersonalNPCHarmonyMod.Instance;
            if (mod == null) return true;

            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;

            return !mod.OnChatCommand(player, message);
        }
    }

    /// <summary>Same routing for hardcore servers where the GUI uses chat.localsay.</summary>
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.localsay))]
    public static class Chat_LocalSay_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;

            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || message[0] != '/') return true;

            var mod = PersonalNPCHarmonyMod.Instance;
            if (mod == null) return true;

            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return true;

            return !mod.OnChatCommand(player, message);
        }
    }
}
