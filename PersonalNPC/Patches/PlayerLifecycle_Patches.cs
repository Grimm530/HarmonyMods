using System;
using HarmonyLib;
using Network;
using UnityEngine;

namespace PersonalNPCHarmony.Patches
{
    /// <summary>Oxide OnPlayerConnected -> BasePlayer.PlayerInit postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, Connection c)
        {
            if (__instance == null || __instance.IsNpc) return;
            try { PersonalNPCHarmonyMod.Instance?.Plugin?.OnPlayerConnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] OnPlayerConnected: " + ex.Message); }
        }
    }

    /// <summary>Oxide OnPlayerDisconnected -> BasePlayer.OnDisconnected postfix (core + helper).</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            var mod = PersonalNPCHarmonyMod.Instance;
            if (mod == null) return;

            try { mod.Plugin?.OnPlayerDisconnected(__instance); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] OnPlayerDisconnected: " + ex.Message); }

            try { mod.Helper?.OnPlayerDisconnected(__instance, "disconnect"); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] Helper OnPlayerDisconnected: " + ex.Message); }
        }
    }

    /// <summary>Oxide OnPlayerRespawned -> BasePlayer.RespawnAt postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    public static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            if (__instance == null || __instance.IsNpc) return;
            try { PersonalNPCHarmonyMod.Instance?.Plugin?.OnPlayerRespawned(__instance); }
            catch (Exception ex) { Debug.LogWarning("[PersonalNPC] OnPlayerRespawned: " + ex.Message); }
        }
    }

    /// <summary>
    /// Oxide OnPlayerDeath -> BasePlayer.Die prefix. PersonalNPC returns false for its own bots so
    /// it can drop a backpack/corpse itself, which means the vanilla death path must be cancelled.
    /// </summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
    public static class BasePlayer_Die_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, HitInfo info)
        {
            if (__instance == null) return true;
            try
            {
                var result = PersonalNPCHarmonyMod.Instance?.Plugin?.OnPlayerDeath(__instance, info);
                if (result != null) return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PersonalNPC] OnPlayerDeath: " + ex.Message);
            }
            return true;
        }
    }
}
