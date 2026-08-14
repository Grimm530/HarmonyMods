using HarmonyLib;

namespace RustLeagueHarmony.Patches
{
    [HarmonyPatch(typeof(PlayerMetabolism), "RunMetabolism")]
    internal static class PlayerMetabolism_RunMetabolism_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerMetabolism __instance, BaseCombatEntity ownerEntity)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null) return;
            plugin.TryKeepEventOxygen(__instance, ownerEntity);
        }
    }
}
