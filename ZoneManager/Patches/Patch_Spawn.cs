using HarmonyLib;
using UnityEngine;
using ZM = Harmony.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    public static class BaseNetworkable_Kill_Patch
    {
        // GrimmCore owns BaseNetworkable.Kill. Handler registered in GrimmCoreHurtRegistration.
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
