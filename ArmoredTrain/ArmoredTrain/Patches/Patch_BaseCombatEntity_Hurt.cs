using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of the ArmoredTrain OnEntityTakeDamage(...) Oxide overloads. A single prefix on
    /// BaseCombatEntity.Hurt dispatches by concrete entity type. When the ported hook returns a
    /// non-null result the damage is blocked (original Hurt is skipped), matching Oxide semantics.
    /// The ported hook may also scale info.damageTypes before returning null (allow-through).
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = ATPlugin.Dispatch_Hurt(__instance, info);
            return result == null; // null -> allow original Hurt; non-null -> block
        }
    }
}
