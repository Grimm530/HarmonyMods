using System;
using HarmonyLib;
using UnityEngine;

namespace AdminMenuHarmony.Patches
{
    /// <summary>Oxide OnPlayerConnected → BasePlayer.PlayerInit postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                AdminMenuHarmonyMod.Instance?.Plugin?.OnPlayerConnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] OnPlayerConnected: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnPlayerDisconnected → BasePlayer.OnDisconnected postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                AdminMenuHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AdminMenu] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }
}
