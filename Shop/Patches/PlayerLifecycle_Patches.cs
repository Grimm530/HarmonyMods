using HarmonyLib;
using System;
using UnityEngine;

namespace ShopHarmony.Patches
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
                ShopHarmonyMod.Instance?.Plugin?.OnPlayerConnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] OnPlayerConnected: " + ex.Message);
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
                ShopHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Oxide OnNewSave — when SaveRestore.Load runs with a wipe id change.
    /// </summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string strFilename, bool allowOutOfDateSaves, bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = AppDomain.CurrentDomain.GetData("Shop_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("Shop_LastWipeId", wipeId);
                if (prev == null) return;
                ShopHarmonyMod.Instance?.Plugin?.OnNewSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Shop] OnNewSave: " + ex.Message);
            }
        }
    }
}
