using HarmonyLib;
using UnityEngine;

namespace ChestStacks.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = ChestStacksMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[ChestStacks] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = ChestStacksMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[ChestStacks] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            var plugin = ChestStacksMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            var box = __instance as BoxStorage;
            if (box == null) return;
            try { plugin.OnEntityKill(box); }
            catch (System.Exception ex) { Debug.LogWarning("[ChestStacks] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(DestroyOnGroundMissing), "OnGroundMissing")]
    internal static class DestroyOnGroundMissing_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(DestroyOnGroundMissing __instance)
        {
            var plugin = ChestStacksMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return true;
            try
            {
                var entity = GameObjectEx.ToBaseEntity(__instance.gameObject);
                var box = entity as BoxStorage;
                if (box == null) return true;
                object blocked = plugin.OnEntityGroundMissing(box);
                if (blocked != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[ChestStacks] OnEntityGroundMissing: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.DoAutomatedSave), typeof(bool))]
    internal static class ServerSave_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            var plugin = ChestStacksMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.SaveData(); }
            catch (System.Exception ex) { Debug.LogWarning("[ChestStacks] OnServerSave: " + ex.Message); }
        }
    }
}
