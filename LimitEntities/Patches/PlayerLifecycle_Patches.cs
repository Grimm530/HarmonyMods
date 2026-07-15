using System;
using HarmonyLib;
using UnityEngine;

namespace LimitEntities.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var service = LimitEntitiesMod.Service;
            if (service == null || !service.IsReady || __instance == null) return;
            service.OnPlayerConnected(__instance);
        }
    }

    /// <summary>Oxide OnNewSave — clear BuildingsOwners on wipe id change.</summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    internal static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(string strFilename, bool allowOutOfDateSaves, bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = AppDomain.CurrentDomain.GetData("LimitEntities_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("LimitEntities_LastWipeId", wipeId);
                if (prev == null) return;

                var service = LimitEntitiesMod.Service;
                if (service == null) return;
                service.StoredDataClear();
                Debug.Log("[LimitEntities] OK: Wipe detected — BuildingsOwners cleared.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LimitEntities] OnNewSave: " + ex.Message);
            }
        }
    }
}
