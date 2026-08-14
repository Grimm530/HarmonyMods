using HarmonyLib;

namespace RustLeagueHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            plugin.OnPlayerDisconnected(__instance);
        }
    }
}
