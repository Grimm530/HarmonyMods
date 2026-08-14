using HarmonyLib;
using UnityEngine;

namespace RadioHarmony.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            if (!(__instance is Minicopter) && !(__instance is AttackHelicopter) && !(__instance is Tugboat))
                return;
            try { RadioMod.Instance?.Vehicles?.OnVehicleSpawned(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Radio] Spawn: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill))]
    internal static class Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            if (__instance == null) return;
            try { RadioMod.Instance?.Vehicles?.OnEntityKilled(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Radio] Kill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    internal static class ServerSave_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { RadioMod.Instance?.Vehicles?.SaveData(); }
            catch (System.Exception ex) { Debug.LogWarning("[Radio] OnServerSave: " + ex.Message); }
        }
    }
}
