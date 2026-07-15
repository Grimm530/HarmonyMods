using HarmonyLib;
using UnityEngine;

namespace LimitEntities.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is not BaseEntity entity || entity is BasePlayer) return;
            var service = LimitEntitiesMod.Service;
            if (service == null || !service.IsReady) return;

            // Growables: only when TrackGrowable; deferred one tick like Oxide
            if (entity is GrowableEntity)
            {
                if (service.Config == null || !service.Config.TrackGrowable) return;
                LimitEntitiesMod.NextTick(() =>
                {
                    if (entity != null && entity.IsValid())
                        service.OnEntitySpawned(entity);
                });
                return;
            }

            service.OnEntitySpawned(entity);
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            if (__instance is not BaseEntity entity) return;
            var service = LimitEntitiesMod.Service;
            if (service == null || !service.IsReady) return;
            service.OnEntityKill(entity);
        }
    }
}
