using HarmonyLib;
using GBPlugin = Harmony.Plugins.GrimmBoss;

namespace GrimmBoss.Patches
{
    /// <summary>Harmony OnPlayerDeath → BasePlayer.Die postfix (proximity player cleanup).</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BasePlayer_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, HitInfo info)
        {
            if (__instance is NPCPlayer) return;
            GBPlugin.Dispatch_OnPlayerDeath(__instance, info);
        }
    }
}
