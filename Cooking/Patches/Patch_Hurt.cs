using HarmonyLib;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = CookingPlugin.Dispatch_OnEntityTakeDamage(__instance, info);
            return result == null;
        }
    }
}
