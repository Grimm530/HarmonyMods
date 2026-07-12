using System;
using HarmonyLib;
using UnityEngine;

namespace RustRewardsHarmony.Patches
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
                RustRewardsHarmonyMod.Instance?.Plugin?.OnPlayerConnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnPlayerConnected: " + ex.Message);
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
                RustRewardsHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance, "");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnServerSave → SaveRestore.Save(bool) postfix.</summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result)
        {
            if (!__result) return;
            try
            {
                RustRewardsHarmonyMod.Instance?.Plugin?.OnServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnServerSave: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Oxide OnNewSave — when SaveRestore.Load runs with a new wipe id.
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
                var prev = AppDomain.CurrentDomain.GetData("RustRewards_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("RustRewards_LastWipeId", wipeId);
                if (prev == null) return; // first observation — store only
                RustRewardsHarmonyMod.Instance?.Plugin?.OnNewSave(strFilename ?? "");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnNewSave: " + ex.Message);
            }
        }
    }
}
