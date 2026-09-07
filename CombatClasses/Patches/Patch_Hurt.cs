using HarmonyLib;
using CCPlugin = Harmony.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = CCPlugin.Dispatch_OnEntityTakeDamage(__instance, info);
            return result == null;
        }
    }
}
