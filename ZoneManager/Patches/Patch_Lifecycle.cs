using HarmonyLib;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { ZM.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { ZM.Dispatch_OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.StartSleeping))]
    public static class BasePlayer_StartSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { ZM.Dispatch_OnPlayerSleep(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnPlayerSleep: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    public static class BasePlayer_EndSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { ZM.Dispatch_OnPlayerSleepEnd(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnPlayerSleepEnd: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(TerrainMeta), nameof(TerrainMeta.PostSetupComponents))]
    public static class TerrainMeta_PostSetupComponents_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { ZM.Dispatch_OnTerrainInitialized(); }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnTerrainInitialized: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ConVar.Global), nameof(ConVar.Global.kill))]
    public static class Global_kill_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var player = args?.Player();
            if (player == null) return true;
            try
            {
                var inst = ZM.GetModInstance();
                if (inst == null || !inst.IsSubscribed("OnServerCommand")) return true;
                if (inst.HarmonyNoSuicide(player))
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] kill: " + ex.Message); }
            return true;
        }
    }
}
