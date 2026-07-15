// Player lifecycle + server save hooks.
using System;
using HarmonyLib;
using UnityEngine;
using TPVE = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class Patch_BasePlayer_PlayerInit
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { TPVE.Dispatch_OnPlayerConnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class Patch_BasePlayer_OnDisconnected
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { TPVE.Dispatch_OnPlayerDisconnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.StartSleeping))]
    public static class Patch_BasePlayer_StartSleeping
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { TPVE.Dispatch_OnPlayerSleep(__instance); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnPlayerSleep: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    public static class Patch_BasePlayer_EndSleeping
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { TPVE.Dispatch_OnPlayerSleepEnded(__instance); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnPlayerSleepEnded: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), new[] { typeof(string), typeof(bool) })]
    public static class Patch_SaveRestore_Save
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { TPVE.Dispatch_OnServerSave(); }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnServerSave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class Patch_SaveRestore_Load
    {
        [HarmonyPostfix]
        public static void Postfix(string strFilename, bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = AppDomain.CurrentDomain.GetData("TruePVE_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("TruePVE_LastWipeId", wipeId);
                if (prev == null) return; // first load, not a wipe
                TPVE.Dispatch_OnNewSave(strFilename ?? "");
            }
            catch (Exception ex) { Debug.LogWarning("[TruePVE] OnNewSave: " + ex.Message); }
        }
    }
}
