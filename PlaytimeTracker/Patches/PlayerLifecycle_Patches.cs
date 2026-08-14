using System;
using HarmonyLib;
using UnityEngine;

namespace PlaytimeTrackerHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            try { PlaytimeTrackerMod.Instance?.OnUserConnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] OnUserConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            try { PlaytimeTrackerMod.Instance?.OnUserDisconnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] OnUserDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    internal static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = AppDomain.CurrentDomain.GetData("PlaytimeTracker_LastWipeId") as string;
                AppDomain.CurrentDomain.SetData("PlaytimeTracker_LastWipeId", wipeId);
                if (prev == null || prev == wipeId) return;
                PlaytimeTrackerMod.Instance?.OnNewSave();
            }
            catch (Exception ex) { Debug.LogWarning("[PlaytimeTracker] OnNewSave: " + ex.Message); }
        }
    }
}
