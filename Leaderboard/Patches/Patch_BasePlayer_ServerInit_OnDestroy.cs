using HarmonyLib;

namespace Leaderboard.Patches;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.ServerInit))]
public static class Patch_BasePlayer_ServerInit
{
    static void Postfix(BasePlayer __instance)
    {
        if (__instance?.net == null) return;
        if (__instance.IsNpc) return;
        if (!SteamIdHelper.IsSteamId(__instance.userID)) return;
        if (__instance.net.connection == null) return; // not yet connected

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
