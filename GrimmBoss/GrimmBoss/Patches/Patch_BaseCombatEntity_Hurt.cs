using HarmonyLib;
using GBPlugin = Oxide.Plugins.GrimmBoss;

namespace GrimmBoss.Patches
{
    /// <summary>
    /// Port of GrimmBoss OnEntityTakeDamage overloads. Non-null hook result blocks Hurt (Oxide semantics).
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = GBPlugin.Dispatch_Hurt(__instance, info);
            return result == null;
        }
    }
}
