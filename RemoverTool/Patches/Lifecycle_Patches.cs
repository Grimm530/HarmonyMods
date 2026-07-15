using System;
using HarmonyLib;
using UnityEngine;

namespace RemoverToolHarmony.Patches
{
    /// <summary>Oxide OnServerSave — SaveRestore.Save(bool) postfix.</summary>
    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(bool))]
    internal static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                RemoverToolHarmonyMod.Instance?.Plugin?.OnServerSave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RemoverTool] OnServerSave: " + ex.Message);
            }
        }
    }

    /// <summary>Oxide OnEntitySpawned — BaseNetworkable.Spawn postfix (only when subscribed).</summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is not BaseEntity entity) return;
            var plugin = RemoverToolHarmonyMod.Instance?.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntitySpawned")) return;
            try { plugin.OnEntitySpawned(entity); }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] OnEntitySpawned: " + ex.Message); }
        }
    }

    /// <summary>Oxide OnEntityKill — BaseNetworkable.Kill prefix (only when subscribed).</summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            if (__instance is not BaseEntity entity) return;
            var plugin = RemoverToolHarmonyMod.Instance?.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntityKill")) return;
            try { plugin.OnEntityKill(entity); }
            catch (Exception ex) { Debug.LogWarning("[RemoverTool] OnEntityKill: " + ex.Message); }
        }
    }
}
