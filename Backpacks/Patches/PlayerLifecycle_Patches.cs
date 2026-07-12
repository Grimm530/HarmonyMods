using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Network;

namespace BackpacksHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                var plugin = BackpacksHarmonyMod.Instance?.Plugin;
                if (plugin == null) return;
                if (!plugin.IsSubscribed(nameof(plugin.OnPlayerConnected))) return;
                plugin.OnPlayerConnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnPlayerConnected: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                BackpacksHarmonyMod.Instance?.Plugin?.OnPlayerDisconnected(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnPlayerDisconnected: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    public static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                BackpacksHarmonyMod.Instance?.Plugin?.OnPlayerRespawned(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnPlayerRespawned: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.StartSleeping))]
    public static class BasePlayer_StartSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                var plugin = BackpacksHarmonyMod.Instance?.Plugin;
                if (plugin == null) return;
                if (!plugin.IsSubscribed(nameof(plugin.OnPlayerSleep))) return;
                plugin.OnPlayerSleep(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnPlayerSleep: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    public static class BasePlayer_EndSleeping_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try
            {
                var plugin = BackpacksHarmonyMod.Instance?.Plugin;
                if (plugin == null) return;
                if (!plugin.IsSubscribed(nameof(plugin.OnPlayerSleepEnded))) return;
                plugin.OnPlayerSleepEnded(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnPlayerSleepEnded: " + ex.Message);
            }
        }
    }

    // OnEntityDeath redirects to OnEntityKill in the plugin. Patch Kill only to avoid double-drop.
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            try
            {
                if (__instance is BasePlayer player)
                    BackpacksHarmonyMod.Instance?.Plugin?.OnEntityKill(player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnEntityKill: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                BackpacksHarmonyMod.Instance?.Plugin?.OnServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnServerSave: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string strFilename, bool allowOutOfDateSaves, bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = AppDomain.CurrentDomain.GetData("Backpacks_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                AppDomain.CurrentDomain.SetData("Backpacks_LastWipeId", wipeId);
                if (prev == null) return;
                BackpacksHarmonyMod.Instance?.Plugin?.OnNewSave(strFilename ?? "");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] OnNewSave: " + ex.Message);
            }
        }
    }
}
