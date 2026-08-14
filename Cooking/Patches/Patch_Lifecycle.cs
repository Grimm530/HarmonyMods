using HarmonyLib;
using UnityEngine;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { CookingPlugin.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { CookingPlugin.Dispatch_OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    public static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { CookingPlugin.Dispatch_OnPlayerRespawned(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnPlayerRespawned: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { CookingPlugin.Dispatch_OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnServerSave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Load))]
    public static class SaveRestore_Load_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(string strFilename, bool __result)
        {
            if (!__result) return;
            try
            {
                var wipeId = SaveRestore.WipeId ?? "";
                var prev = System.AppDomain.CurrentDomain.GetData("Cooking_LastWipeId") as string;
                if (prev != null && prev == wipeId) return;
                System.AppDomain.CurrentDomain.SetData("Cooking_LastWipeId", wipeId);
                if (prev == null) return;
                CookingPlugin.Dispatch_OnNewSave(strFilename ?? "");
            }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnNewSave: " + ex.Message); }
        }
    }
}
