// OnEntityTakeDamage — prefix on BaseCombatEntity.Hurt(HitInfo).
// Returning false cancels the damage (matching Oxide "non-null return = cancel" semantics).
// The plugin may also modify info.damageTypes before returning null (allow-through).
using HarmonyLib;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = STPlugin.Dispatch_OnEntityTakeDamage(__instance, info);
            return result == null; // null -> allow; non-null -> block
        }
    }
}
