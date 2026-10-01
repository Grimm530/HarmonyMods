using HarmonyLib;

namespace PveModeHarmony.Patches
{
    /// <summary>
    /// Blocks damage inside PveMode event zones (non-owner/non-team attacker vs event-tagged
    /// NPC/Bradley/Helicopter/Turret), and accumulates ownership damage while an event has no owner.
    /// Mirrors Oxide PveMode's OnEntityTakeDamage: true = block damage.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null || info == null) return true;
            object result = PveModeManager.OnEntityTakeDamage(__instance, info);
            return !(result is bool blocked && blocked);
        }
    }
}
