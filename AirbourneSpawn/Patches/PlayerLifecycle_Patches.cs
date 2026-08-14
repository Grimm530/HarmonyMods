using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
    internal static class BasePlayer_Die_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDeath(__instance); }
            catch (System.Exception ex) { Hooks.Warn("OnPlayerDeath", ex); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerDisconnected(__instance); }
            catch (System.Exception ex) { Hooks.Warn("OnPlayerDisconnected", ex); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.RespawnAt))]
    internal static class BasePlayer_RespawnAt_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || __instance == null) return;
            try { plugin.OnPlayerRespawned(__instance); }
            catch (System.Exception ex) { Hooks.Warn("OnPlayerRespawned", ex); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.GetRespawnOptionsForPlayer))]
    internal static class BasePlayer_GetRespawnOptionsForPlayer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(System.Collections.Generic.List<ProtoBuf.RespawnInformation.SpawnOptions> spawnOptions, ulong userID)
        {
            var plugin = Hooks.Plugin;
            if (plugin == null || spawnOptions == null) return;
            try
            {
                BasePlayer player = BasePlayer.FindByID(userID);
                if (player == null)
                {
                    for (int i = 0; i < BasePlayer.activePlayerList.Count; i++)
                    {
                        BasePlayer p = BasePlayer.activePlayerList[i];
                        if (p != null && p.GetUserId() == userID)
                        {
                            player = p;
                            break;
                        }
                    }
                }
                if (player == null) return;
                plugin.OnRespawnInformationGiven(player, spawnOptions);
            }
            catch (System.Exception ex) { Hooks.Warn("OnRespawnInformationGiven", ex); }
        }
    }
}
