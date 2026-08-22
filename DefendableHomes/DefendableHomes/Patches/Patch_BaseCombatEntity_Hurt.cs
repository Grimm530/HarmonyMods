using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    /// <summary>
    /// Port of OnEntityTakeDamage(ScientistNPC). Non-null hook result blocks Hurt (Oxide semantics).
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = DHPlugin.Dispatch_Hurt(__instance, info);
            return result == null;
        }
    }
}
