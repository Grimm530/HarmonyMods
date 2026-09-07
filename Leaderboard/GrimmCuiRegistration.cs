using System;
using GrimmCuiHarmony;

namespace Leaderboard
{
    internal static class GrimmCuiRegistration
    {
        internal static void Register()
        {
            GrimmCui.RegisterEndtest("LEADERBOARD", (_, src) =>
            {
                var a = src.Args; if (a == null || a.Length < 2) return;
                var mod = LeaderboardMod.Instance; if (mod == null) return;
                var player = src.Connection?.player as BasePlayer; if (player == null) return;
                if (a.GetValue(1)?.ToString() == "close")
                {
                    mod.OnLeaderboardClosed(player.userID);
                    mod.ExitServerPanelMode(player);
                    LeaderboardUI.Destroy(player);
                    return;
                }
                if (a.GetValue(1)?.ToString() == "page" && a.Length >= 3 && int.TryParse(a.GetValue(2)?.ToString(), out var cat))
                {
                    if (cat == 0) mod.ClearViewedProfile(player.userID);
                    mod.SetLeaderboardCategory(player.userID, cat);
                    LeaderboardUI.Show(player); return;
                }
                if (a.GetValue(1)?.ToString() == "viewprofile" && a.Length >= 3 && ulong.TryParse(a.GetValue(2)?.ToString(), out var targetId))
                {
                    mod.SetViewedProfile(player.userID, targetId);
                    mod.SetLeaderboardCategory(player.userID, 0);
                    mod.SetLeaderboardProfileTab(player.userID, 0);
                    LeaderboardUI.Show(player); return;
                }
                if (a.GetValue(1)?.ToString() == "tab" && a.Length >= 3 && int.TryParse(a.GetValue(2)?.ToString(), out var tab))
                {
                    int category = mod.GetLeaderboardCategory(player.userID);
                    if (category == 1) mod.SetLeaderboardTop10Tab(player.userID, tab);
                    else mod.SetLeaderboardProfileTab(player.userID, tab);
                    LeaderboardUI.Show(player);
                }
            });
        }
    }
}
