using HarmonyLib;
using System;
using UnityEngine;

namespace WipeScheduleHarmony.Patches
{
    /// <summary>Oxide OnPlayerDisconnected → BasePlayer.OnDisconnected postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                WipeScheduleHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WipeSchedule] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }
}
