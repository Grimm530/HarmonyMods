using HarmonyLib;
using UnityEngine;
using HPlugin = Oxide.Plugins.Hud;

namespace HudHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { HPlugin.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { HPlugin.Dispatch_OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.StartSleeping))]
    public static class BasePlayer_StartSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { HPlugin.Dispatch_OnPlayerSleep(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnPlayerSleep: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    public static class BasePlayer_EndSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsSleeping()) return;
            try { HPlugin.Dispatch_OnPlayerSleepEnded(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnPlayerSleepEnded: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { HPlugin.Dispatch_OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnServerSave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ConnectionQueue), "Join")]
    public static class ConnectionQueue_Join_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Network.Connection connection)
        {
            try { HPlugin.Dispatch_OnConnectionQueue(connection); }
            catch (System.Exception ex) { Debug.LogWarning("[Hud] OnConnectionQueue: " + ex.Message); }
        }
    }
}
