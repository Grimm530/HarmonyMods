using HarmonyLib;
using UnityEngine;

namespace KaruzaVehicles.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            var mod = KaruzaVehiclesMod.Instance;
            if (mod == null || __instance == null) return;

            try
            {
                if (__instance is RidableHorse horse)
                    mod.HorseTowing?.OnEntitySpawned(horse);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] OnEntitySpawned(RidableHorse): " + ex.Message);
            }

            try
            {
                if (__instance is CargoShip cargo &&
                    mod.Common != null &&
                    mod.Common.IsSubscribed(nameof(KaruzaEntitiesCommon.OnEntitySpawned)))
                {
                    mod.Common.OnEntitySpawned(cargo);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] OnEntitySpawned(CargoShip): " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            var mod = KaruzaVehiclesMod.Instance;
            if (mod == null || __instance == null) return;

            try
            {
                if (__instance is BaseEntity entity)
                    mod.CustomEntities?.OnEntityKill(entity);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] OnEntityKill: " + ex.Message);
            }

            try
            {
                if (__instance is CargoShip cargo &&
                    mod.Common != null &&
                    mod.Common.IsSubscribed(nameof(KaruzaEntitiesCommon.OnEntityKill)))
                {
                    mod.Common.OnEntityKill(cargo);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] OnEntityKill(CargoShip): " + ex.Message);
            }
        }
    }
}
