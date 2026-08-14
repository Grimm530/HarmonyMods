using HarmonyLib;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (!(__instance is BaseEntity entity)) return;
            try
            {
                if (Deployer_DoDeploy_Patch.Pending != null)
                {
                    var deployer = Deployer_DoDeploy_Patch.Pending;
                    var item = deployer.GetItem();
                    var mod = item?.info?.GetComponent<ItemModDeployable>();
                    if (mod != null)
                        ZM.Dispatch_OnItemDeployed(deployer, mod, entity);
                    else
                        ZM.Dispatch_OnItemDeployed(deployer, deployer.GetParentEntity(), entity);
                }
                ZM.Dispatch_OnEntitySpawned(entity);
            }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnEntitySpawned: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (!(__instance is BaseEntity entity)) return;
            try { ZM.Dispatch_OnEntityKill(entity); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Deployer), nameof(Deployer.DoDeploy_Regular))]
    public static class Deployer_DoDeploy_Patch
    {
        internal static Deployer Pending;

        [HarmonyPrefix]
        public static void Prefix(Deployer __instance) => Pending = __instance;

        [HarmonyPostfix]
        public static void Postfix() => Pending = null;
    }

    [HarmonyPatch(typeof(Deployer), nameof(Deployer.DoDeploy_Slot))]
    public static class Deployer_DoDeploy_Slot_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Deployer __instance) => Deployer_DoDeploy_Patch.Pending = __instance;

        [HarmonyPostfix]
        public static void Postfix() => Deployer_DoDeploy_Patch.Pending = null;
    }
}
