using HarmonyLib;
using System;
using UnityEngine;

namespace EconomicsHarmony.Patches
{
    /// <summary>Oxide OnUserConnected → BasePlayer.PlayerInit postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                var iplayer = __instance?.ToIPlayer();
                if (iplayer != null)
                    EconomicsHarmonyMod.Instance?.Plugin?.OnUserConnected(iplayer);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Economics] OnUserConnected: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnUserDisconnected → BasePlayer.OnDisconnected postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                var iplayer = __instance?.ToIPlayer();
                if (iplayer != null)
                    EconomicsHarmonyMod.Instance?.Plugin?.OnUserDisconnected(iplayer);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Economics] OnUserDisconnected: " + ex.Message);
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
                var prev = AppDomain.CurrentDomain.GetData("Economics_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("Economics_LastWipeId", wipeId);
                if (prev == null) return; // first observation — store only
                EconomicsHarmonyMod.Instance?.Plugin?.OnNewSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Economics] OnNewSave: " + ex.Message);
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
                EconomicsHarmonyMod.Instance?.Plugin?.OnServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Economics] OnServerSave: " + ex.Message);
            }
        }
    }
}
