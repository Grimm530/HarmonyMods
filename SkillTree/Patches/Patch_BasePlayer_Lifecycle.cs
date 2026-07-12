// Player lifecycle patches: Connected, Disconnected, Respawned.
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    /// <summary>OnPlayerConnected — BasePlayer.PlayerInit postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { STPlugin.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerConnected: " + ex.Message); }
        }
    }

    /// <summary>OnPlayerDisconnected — BasePlayer.OnDisconnected postfix (reason = empty).</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { STPlugin.Dispatch_OnPlayerDisconnected(__instance, ""); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    /// <summary>OnPlayerRespawned — BasePlayer.RespawnAt postfix.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    public static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { STPlugin.Dispatch_OnPlayerRespawned(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnPlayerRespawned: " + ex.Message); }
        }
    }
}
