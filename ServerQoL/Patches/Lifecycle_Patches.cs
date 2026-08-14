using HarmonyLib;
using UnityEngine;

namespace ServerQoL.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            ServerQoLService service = ServerQoLMod.Service;
            if (service == null || __instance == null) return;
            if (__instance is BasePlayer) return;

            try
            {
                if (__instance is Candle || __instance is TorchWeapon || __instance is ElectricGenerator || __instance is NPCVendingMachine)
                    service.OnEntitySpawned(__instance);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ServerQoL] OnEntitySpawned: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            ServerQoLService service = ServerQoLMod.Service;
            if (service == null || __instance == null) return;
            try
            {
                service.OnPlayerInit(__instance);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ServerQoL] PlayerInit: " + ex.Message);
            }
        }
    }
}
