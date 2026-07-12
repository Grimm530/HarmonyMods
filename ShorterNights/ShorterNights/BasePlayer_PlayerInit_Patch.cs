using HarmonyLib;

namespace ShorterNights;

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
internal static class BasePlayer_PlayerInit_Patch
{
    [HarmonyPostfix]
    public static void Postfix(BasePlayer __instance)
    {
        if (__instance == null || !__instance.IsConnected) return;
        if (ShorterNightsConfig.Config?.ShowTimeOfDayDisplay != true) return;
        var player = __instance;
        InvokeHandler.Invoke(player, () =>
        {
            if (player != null && !player.IsDestroyed && player.IsConnected)
                GameTimeDisplayUI.RefreshFor(player);
        }, 3f);
    }
}
