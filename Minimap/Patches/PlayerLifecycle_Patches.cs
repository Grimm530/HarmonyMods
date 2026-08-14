using System;
using HarmonyLib;
using UnityEngine;

namespace MinimapHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnPlayerConnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnPlayerConnected: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Snapshot is finished here ("has spawned"). AddUI sent during PlayerInit is dropped by the client.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), "EnterGame")]
    public static class BasePlayer_EnterGame_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnPlayerEnteredGame(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnPlayerEnteredGame: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    public static class BasePlayer_EndSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnPlayerSleepEnded(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnPlayerSleepEnded: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                MinimapHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }
}
