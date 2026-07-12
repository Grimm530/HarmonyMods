using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of ArmoredTrain OnPlayerSleep(BasePlayer): removes a sleeping player from the event zone
    /// tracking so zone effects stop applying.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.StartSleeping))]
    public static class Patch_BasePlayer_StartSleeping
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            ATPlugin.Dispatch_OnPlayerSleep(__instance);
        }
    }
}
