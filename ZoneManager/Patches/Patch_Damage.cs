using HarmonyLib;
using UnityEngine;
using ZM = Harmony.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
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
