using HarmonyLib;
using UnityEngine;

namespace KaruzaVehicles.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var push = KaruzaVehiclesMod.Instance?.VehiclePush;
            if (push == null || __instance == null) return;
            try { push.OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[KaruzaVehicles] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var push = KaruzaVehiclesMod.Instance?.VehiclePush;
            if (push == null || __instance == null) return;
            try { push.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[KaruzaVehicles] OnPlayerDisconnected: " + ex.Message); }
        }
    }
}
