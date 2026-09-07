using HarmonyLib;

namespace PlatformSync.Patches
{
    /// <summary>Postfix on BasePlayer.PlayerInit — player fully connected (Harmony OnPlayerConnected).</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (!__instance.IsConnected) return;
            PlatformSyncPlugin.Instance?.OnPlayerConnected(__instance);
        }
    }
}
