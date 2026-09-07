using HarmonyLib;

namespace LoadingMessages.Patches
{
    /// <summary>
    /// Harmony OnPlayerConnected equivalent — BasePlayer.PlayerInit postfix.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            LoadingMessagesMod.Instance?.OnPlayerConnected(__instance);
        }
    }
}
