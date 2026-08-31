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
    /// Oxide OnNewSave: SaveRestore.Load returns false when there is no save file
    /// (map wipe / fresh world). That is the wipe — not a successful load.
    /// </summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string strFilename, bool __result)
        {
            if (__result) return;
            try
            {
                KitsHarmonyMod.Instance?.Plugin?.OnNewSave(strFilename ?? "");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] OnNewSave: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// WipeId is assigned here (from the save, or a new guid after a wipe).
    /// Persist it to disk so AutoWipe still runs after a process restart.
    /// </summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.InitializeWipeId))]
    public static class SaveRestore_InitializeWipeId_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                KitsHarmonyMod.CheckPersistedWipeId();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Kits] CheckPersistedWipeId: " + ex.Message);
            }
        }
    }
}
