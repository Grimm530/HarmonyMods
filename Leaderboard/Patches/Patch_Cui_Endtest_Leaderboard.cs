using HarmonyLib;
using Network;

namespace Leaderboard.Patches;

/// <summary>
/// Intercepts cui.endtest when used for leaderboard UI (category switch or close).
/// Buttons use "cui.endtest LEADERBOARD page 0" or "cui.endtest LEADERBOARD close".
/// </summary>
[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Patch_Cui_Endtest_Leaderboard
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg args)
    {
        var a = args?.Args;
        if (a == null || a.Length < 2 || a[0] != "LEADERBOARD")
            return true;

        var mod = LeaderboardMod.Instance;
        if (mod == null) return true;

        var player = args.Connection?.player as BasePlayer;
        if (player == null) return true;

        if (a[1] == "close")
        {
            bool inPanel = mod.IsOpenInServerPanel(player.userID);
            if (inPanel)
                LeaderboardUI.DestroyServerPanel(player);
            else
                LeaderboardUI.Destroy(player);
            mod.OnLeaderboardClosed(player.userID);
            return false;
        }

        if (a[1] == "page" && a.Length >= 3 && int.TryParse(a[2], out var cat))
        {
            if (cat == 0)
                mod.ClearViewedProfile(player.userID);
            mod.SetLeaderboardCategory(player.userID, cat);
            mod.RefreshLeaderboardUI(player);
            return false;
        }

        if (a[1] == "viewprofile" && a.Length >= 3 && ulong.TryParse(a[2], out var targetId))
        {
            mod.SetViewedProfile(player.userID, targetId);
            mod.SetLeaderboardCategory(player.userID, 0);
            mod.SetLeaderboardProfileTab(player.userID, 0); // show General (profile) tab, not e.g. Hitrate
            mod.RefreshLeaderboardUI(player);
            return false;
        }

        if (a[1] == "tab" && a.Length >= 3 && int.TryParse(a[2], out var tab))
        {
            int category = mod.GetLeaderboardCategory(player.userID);
            if (category == 1)
                mod.SetLeaderboardTop10Tab(player.userID, tab);
            else
                mod.SetLeaderboardProfileTab(player.userID, tab);
            mod.RefreshLeaderboardUI(player);
            return false;
        }

        return true;
    }
}
