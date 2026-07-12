using HarmonyLib;

namespace LoadingMessages.Patches
{
    /// <summary>
    /// Oxide OnPlayerConnected equivalent — BasePlayer.PlayerInit postfix.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            LoadingMessagesMod.Instance?.OnPlayerConnected(__instance);
        }
    }
}
