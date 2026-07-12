using ConVar;
using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(Chat), nameof(Chat.say))]
public static class Patch_Chat_Say
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;
        var msg = arg.GetString(0, "text")?.Trim();
        if (string.IsNullOrEmpty(msg)) return true;
        var player = arg.Connection?.player as BasePlayer;
        if (player == null) return true;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return true;

        var lower = msg.ToLowerInvariant();
        if (lower.StartsWith("/leaderboard") || lower.StartsWith("/lb") || lower.StartsWith("/stats"))
        {
            mod.OpenLeaderboard(player);
            return false; // suppress chat
        }
        return true;
    }
}
