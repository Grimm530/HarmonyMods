using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;

            try
            {
                if (__instance is BuildingPrivlidge cupboard)
                    plugin.OnEntitySpawned(cupboard);
                else if (__instance is AutoTurret turret)
                    plugin.OnEntitySpawned(turret);
                else if (__instance is CodeLock codeLock)
                    plugin.OnEntitySpawned(codeLock);
                else if (__instance is Workbench workbench)
                    plugin.OnBuildingWorkbenchSpawned(workbench);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] OnEntitySpawned: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;

            try
            {
                if (__instance is BuildingPrivlidge cupboard)
                    plugin.OnEntityKill(cupboard);
                else if (__instance is AutoTurret turret)
                    plugin.OnEntityKill(turret);
                else if (__instance is GunTrap gunTrap)
                    plugin.OnEntityKill(gunTrap);
                else if (__instance is FlameTurret flameTurret)
                    plugin.OnEntityKill(flameTurret);
                else if (__instance is CodeLock codeLock)
                    plugin.OnEntityKill(codeLock);
                else if (__instance is Workbench workbench)
                    plugin.OnBuildingWorkbenchKilled(workbench);
                else if (__instance is PlayerBoatPrivilege boatPriv)
                    plugin.OnBuildingWorkbenchBoatCleared(boatPriv);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] OnEntityKill: " + ex.Message);
            }
        }
    }
}
