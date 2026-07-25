using HarmonyLib;

namespace Leaderboard.Patches;

/// <summary>
/// Oxide OnPlayerConnected equivalent. Use PlayerInit (not ServerInit):
/// ServerInit often runs before net.connection exists, so a null-connection guard
/// would skip session start and playtime never accrues.
/// </summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
public static class Patch_BasePlayer_PlayerInit
{
    static void Postfix(BasePlayer __instance)
    {
        if (__instance == null) return;
        if (__instance.IsNpc) return;
        if (!SteamIdHelper.IsSteamId(__instance.userID)) return;

        LeaderboardMod.Instance?.OnPlayerConnected(__instance);
    }
}

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
public static class Patch_BasePlayer_OnDisconnected
{
    static void Prefix(BasePlayer __instance)
    {
        if (__instance == null) return;
        LeaderboardMod.Instance?.OnPlayerDisconnected(__instance);
    }
}
