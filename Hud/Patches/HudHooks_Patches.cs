using HarmonyLib;
using UnityEngine;
using HPlugin = Oxide.Plugins.Hud;

namespace HudHarmony.Patches
{
    [HarmonyPatch(typeof(BaseMountable), "MountPlayer", new[] { typeof(BasePlayer) })]
    public static class BaseMountable_MountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance is ComputerStation station)
            {
                try { HPlugin.Dispatch_OnEntityMounted(station, player); }
                catch (System.Exception ex) { Debug.LogWarning("[Hud] OnEntityMounted: " + ex.Message); }
            }
        }
    }

    [HarmonyPatch(typeof(BaseMountable), "DismountPlayer", new[] { typeof(BasePlayer), typeof(bool) })]
    public static class BaseMountable_DismountPlayer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseMountable __instance, BasePlayer player)
        {
            if (__instance is ComputerStation station)
            {
                try { HPlugin.Dispatch_OnEntityDismounted(station, player); }
                catch (System.Exception ex) { Debug.LogWarning("[Hud] OnEntityDismounted: " + ex.Message); }
            }
        }
    }

    [HarmonyPatch(typeof(DeepSeaManager), nameof(DeepSeaManager.OpenDeepSea))]
    public static class DeepSeaManager_OpenDeepSea_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(DeepSeaManager __instance)
        {
            try { HPlugin.Dispatch_OnDeepSeaOpened(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnDeepSeaOpened: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(DeepSeaManager), nameof(DeepSeaManager.CloseDeepSea))]
    public static class DeepSeaManager_CloseDeepSea_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(DeepSeaManager __instance)
        {
            try { HPlugin.Dispatch_OnDeepSeaClosed(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnDeepSeaClosed: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            try { HPlugin.Dispatch_OnEntitySpawned(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnEntitySpawned: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            try { HPlugin.Dispatch_OnEntityKill(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(CargoShip), nameof(CargoShip.OnArrivedAtHarbor))]
    public static class CargoShip_OnArrivedAtHarbor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CargoShip __instance)
        {
            try { HPlugin.Dispatch_OnCargoShipHarborArrived(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnCargoShipHarborArrived: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(CargoShip), "LeaveHarbor")]
    public static class CargoShip_LeaveHarbor_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(CargoShip __instance)
        {
            try { HPlugin.Dispatch_OnCargoShipHarborLeave(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnCargoShipHarborLeave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(HackableLockedCrate), nameof(HackableLockedCrate.StartHacking))]
    public static class HackableLockedCrate_StartHacking_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(HackableLockedCrate __instance)
        {
            try { HPlugin.Dispatch_OnCrateHack(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnCrateHack: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(HackableLockedCrate), nameof(HackableLockedCrate.HackProgress))]
    public static class HackableLockedCrate_HackProgress_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(HackableLockedCrate __instance)
        {
            if (__instance == null) return;
            try
            {
                if (__instance.hackSeconds + 1f > HackableLockedCrate.requiredHackSeconds)
                    HPlugin.Dispatch_OnCrateHackEnd(__instance);
            }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnCrateHackEnd: " + ex.Message); }
        }
    }
}
