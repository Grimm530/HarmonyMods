using HarmonyLib;
using UnityEngine;

namespace KaruzaVehicles.Patches
{
    internal static class BaseNetworkable_Spawn_Patch
    {
        public static void Postfix(BaseNetworkable __instance)
        {
            var mod = KaruzaVehiclesMod.Instance;
            if (mod == null) return;

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

    internal static class BaseNetworkable_Kill_Patch
    {
        public static void Prefix(BaseNetworkable __instance)
        {
            var mod = KaruzaVehiclesMod.Instance;
            if (mod == null) return;

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
