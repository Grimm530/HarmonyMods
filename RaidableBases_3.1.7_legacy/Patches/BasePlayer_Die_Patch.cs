/*
 * Invokes RaidableBases OnPlayerDeath hook. Prefix: if hook returns non-null, block default death (Oxide behavior).
 */
using HarmonyLib;
using UnityEngine;

namespace RaidableBases
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), typeof(HitInfo))]
    internal static class BasePlayer_Die_Patch
    {
        [HarmonyPrefix]
        static bool Prefix(BasePlayer __instance, HitInfo info)
        {
            if (__instance == null)
                return true;
            var result = Interface.CallHook("OnPlayerDeath", __instance, info);
            if (result != null)
                return false;
            return true;
        }
    }
}
