using System;
using HarmonyLib;
using UnityEngine;

namespace MinimapHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    public static class BasePlayer_Die_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, HitInfo info)
        {
            if (!HeatMapService.IsEnabled)
                return;
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnPvpDeath(__instance, info);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] PVP heat death: " + ex.Message);
            }
        }
    }
}
