using HarmonyLib;

namespace RustLeagueHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    internal static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || __instance == null || info == null) return;
            plugin.TryNegateEventDamage(__instance, info);
        }
    }
}
