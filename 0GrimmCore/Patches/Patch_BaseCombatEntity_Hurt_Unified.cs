using HarmonyLib;
using UnityEngine;

namespace GrimmCoreHarmony.Patches
{
    /// <summary>
    /// Single owner patch for BaseCombatEntity.Hurt(HitInfo). All mods register via GrimmCoreHurtDispatcher.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    internal static class Patch_BaseCombatEntity_Hurt_Unified
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseCombatEntity __instance, HitInfo info, ref bool __state)
        {
            __state = false;
            return GrimmCoreHurtDispatcher.InvokePrefix(__instance, info, ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(BaseCombatEntity __instance, HitInfo info, bool __state)
        {
            if (__state)
                return;
            GrimmCoreHurtDispatcher.InvokePostfix(__instance, info);
        }
    }
}
