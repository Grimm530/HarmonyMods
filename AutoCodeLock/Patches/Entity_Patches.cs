using Facepunch.Rust;
using HarmonyLib;
using UnityEngine;

namespace AutoCodeLockHarmony.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;

            try
            {
                if (__instance is CodeLock codeLock)
                    plugin.OnEntitySpawnedCodeLock(codeLock);
                else if (__instance is DoorCloser doorCloser)
                    plugin.OnEntitySpawnedDoorCloser(doorCloser);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] OnEntitySpawned: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;

            try
            {
                if (__instance is CodeLock codeLock)
                    plugin.OnEntityKillCodeLock(codeLock);
                else if (__instance is DoorCloser doorCloser)
                    plugin.OnEntityKillDoorCloser(doorCloser);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] OnEntityKill: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Deployer), nameof(Deployer.DoDeploy_Slot))]
    internal static class Deployer_DoDeploy_Slot_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Deployer __instance, Deployable deployable, Ray ray, NetworkableId entityID)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;

            try
            {
                BaseNetworkable ent = BaseNetworkable.serverEntities.Find(entityID);
                if (ent is BaseEntity baseEntity)
                    plugin.OnItemDeployed(__instance, baseEntity, deployable);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] OnItemDeployed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Reliable Oxide OnEntityBuilt replacement — fires for construction and deployables
    /// after OwnerID / player are assigned (Planner.DoBuild alone often sees OwnerID == 0).
    /// </summary>
    [HarmonyPatch(typeof(Analytics.Azure), nameof(Analytics.Azure.OnEntityBuilt))]
    internal static class Analytics_OnEntityBuilt_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseEntity entity, BasePlayer player)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || entity == null || player == null) return;

            try
            {
                plugin.OnEntityBuilt(entity, player);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] OnEntityBuilt: " + ex.Message);
            }
        }
    }

    // Backup for construction if Analytics path is skipped for some builds.
    [HarmonyPatch(typeof(Planner), "DoBuild", typeof(Construction.Target), typeof(Construction))]
    internal static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Planner __instance, Construction.Target target, Construction component, BaseEntity __result)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null || __result == null) return;

            try
            {
                plugin.OnEntityBuilt(__instance, __result.gameObject);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] OnEntityBuilt(Planner): " + ex.Message);
            }
        }
    }
}
