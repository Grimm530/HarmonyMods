using HarmonyLib;
using System;
using UnityEngine;

namespace KitsHarmony.Patches
{
    /// <summary>Oxide OnPlayerRespawned → BasePlayer.RespawnAt postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    public static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                KitsHarmonyMod.Instance?.Plugin?.OnPlayerRespawned(__instance);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Kits] OnPlayerRespawned: " + ex.Message);
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
                KitsHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Kits] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnPlayerDeath → BasePlayer.Die postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
    public static class BasePlayer_Die_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, HitInfo info)
        {
            try
            {
                KitsHarmonyMod.Instance?.Plugin?.OnPlayerDeath(__instance, info);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Kits] OnPlayerDeath: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Oxide OnNewSave — when SaveRestore.Load runs with a new wipe.
    /// Calls Kits.OnNewSave so AutoWipe can clear player kit data.
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
                // Treat successful load as potential new-save signal for AutoWipe.
                // Oxide fires OnNewSave only on wipe; we approximate via WipeId change stored in AppDomain.
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = AppDomain.CurrentDomain.GetData("Kits_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("Kits_LastWipeId", wipeId);
                // Skip first load after mod start if we just set the id (no previous)
                if (prev == null)
                {
                    // First observation — still call OnNewSave only if AutoWipe and this looks like a fresh wipe.
                    // Safer: call OnNewSave when prev was set and changed. First boot: store only.
                    return;
                }
                KitsHarmonyMod.Instance?.Plugin?.OnNewSave(strFilename ?? "");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Kits] OnNewSave: " + ex.Message);
            }
        }
    }
}
