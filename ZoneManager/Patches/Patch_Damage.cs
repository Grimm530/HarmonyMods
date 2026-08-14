using HarmonyLib;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            object result = ZM.Dispatch_OnEntityTakeDamage(__instance, info);
            return result == null;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EligibleForWounding))]
    public static class Patch_EligibleForWounding
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, HitInfo info, ref bool __result)
        {
            object result = ZM.Dispatch_CanBeWounded(__instance, info);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }
}
