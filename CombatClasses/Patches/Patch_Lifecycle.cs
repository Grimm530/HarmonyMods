using HarmonyLib;
using UnityEngine;
using CCPlugin = Oxide.Plugins.CombatClasses;

namespace CombatClassesHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { CCPlugin.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { CCPlugin.Dispatch_OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    public static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { CCPlugin.Dispatch_OnPlayerRespawn(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerRespawn: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    public static class BasePlayer_EndSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsSleeping()) return;
            try { CCPlugin.Dispatch_OnPlayerSleepEnded(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnPlayerSleepEnded: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { CCPlugin.Dispatch_OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[CombatClasses] OnServerSave: " + ex.Message); }
        }
    }
}
