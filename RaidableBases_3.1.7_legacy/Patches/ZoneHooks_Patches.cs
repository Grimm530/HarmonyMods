using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    /// <summary>
    /// Oxide Subscribe hooks that RaidableBases handlers implement but Harmony must invoke explicitly.
    /// </summary>
    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    internal static class Planner_DoBuild_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Planner __instance, Construction.Target target, Construction component)
        {
            var result = Interface.CallHook("CanBuild", __instance, component, target);
            return result == null;
        }

        [HarmonyPostfix]
        private static void Postfix(Planner __instance, Construction.Target target, Construction component, BaseEntity __result)
        {
            if (__result == null || __instance == null) return;
            Interface.CallHook("OnEntityBuilt", __instance, __result.gameObject);
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity))]
    internal static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity)
        {
            if (__instance == null || targetEntity == null) return true;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return true;
            var result = Interface.CallHook("CanLootEntity", player, targetEntity);
            // Oxide: non-null blocks looting (often returns true).
            return result == null;
        }
    }

    [HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerStoppedLooting))]
    internal static class StorageContainer_PlayerStoppedLooting_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(StorageContainer __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            Interface.CallHook("OnLootEntityEnd", player, __instance);
        }
    }

    [HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.DoUpgradeToGrade))]
    internal static class BuildingBlock_DoUpgradeToGrade_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BuildingBlock __instance, BaseEntity.RPCMessage msg)
        {
            var player = msg.player;
            if (__instance == null || player == null) return true;
            // Grade/skin parsed inside original; pass current for CanBuild-style block.
            var result = Interface.CallHook("OnStructureUpgrade", __instance, player, __instance.grade, __instance.skinID);
            return result == null;
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Storage_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            if (__instance is Fridge fridge)
                Interface.CallHook("OnEntityKill", fridge);
            else if (__instance is StorageContainer container)
                Interface.CallHook("OnEntityKill", container);
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
    internal static class BasePlayer_EndSleeping_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            Interface.CallHook("OnPlayerSleepEnded", __instance);
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "Server_AddMarker")]
    internal static class BasePlayer_Server_AddMarker_Patch
    {
        static bool Prepare() => AccessTools.Method(typeof(BasePlayer), "Server_AddMarker") != null;

        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
        {
            if (__instance == null) return;
            try
            {
                var note = __instance.State?.pointsOfInterest;
                if (note == null || note.Count == 0) return;
                Interface.CallHook("OnMapMarkerAdded", __instance, note[note.Count - 1]);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Respawn))]
    internal static class BasePlayer_Respawn_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BasePlayer __instance)
        {
            if (__instance == null)
                return true;

            BasePlayer.SpawnPoint spawnPoint = ServerMgr.FindSpawnPoint(__instance);
            if (ConVar.Server.respawnAtDeathPosition && __instance.ServerCurrentDeathNote != null)
                spawnPoint.pos = __instance.ServerCurrentDeathNote.worldPosition;

            var result = Interface.CallHook("OnPlayerRespawn", __instance, spawnPoint);
            if (result is BasePlayer.SpawnPoint replacement)
                spawnPoint = replacement;

            __instance.RespawnAt(spawnPoint.pos, spawnPoint.rot);
            return false;
        }
    }
}
