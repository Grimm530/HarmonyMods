using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    /// <summary>Oxide OnPlayerDeath — exit the event zone when a participating player dies.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BasePlayer_Die
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, HitInfo info)
        {
            DHPlugin.Dispatch_OnPlayerDeath(__instance, info);
        }
    }
}
