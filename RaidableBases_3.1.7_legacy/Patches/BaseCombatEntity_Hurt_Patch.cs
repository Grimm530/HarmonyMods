/*
 * Oxide: OnEntityTakeDamage / CanEntityTakeDamage — applies profile NPC accuracy,
 * damage multipliers, dome damage rules, etc. from each difficulty profile's NPCs block.
 */
using HarmonyLib;

namespace RaidableBases
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), typeof(HitInfo))]
    internal static class BaseCombatEntity_Hurt_Patch
    {
        [HarmonyPrefix]
        static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null || info == null)
            {
                return true;
            }

            // Only one of these is subscribed (PVE vs PVP); CallHook no-ops if unsubscribed.
            var can = Interface.CallHook("CanEntityTakeDamage", __instance, info);
            if (can is bool allowCan && !allowCan)
            {
                return false;
            }

            var on = Interface.CallHook("OnEntityTakeDamage", __instance, info);
            if (on is bool allowOn && !allowOn)
            {
                return false;
            }

            return true;
        }
    }
}
