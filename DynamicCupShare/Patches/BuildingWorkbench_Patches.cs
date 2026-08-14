using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(TriggerWorkbench), "OnEntityEnter")]
    internal static class TriggerWorkbench_OnEntityEnter_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseEntity ent)
        {
            if (ent is not BasePlayer player || player.IsNpc) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnWorkbenchTriggerChanged(player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Workbench trigger enter: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(TriggerWorkbench), "OnEntityLeave")]
    internal static class TriggerWorkbench_OnEntityLeave_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseEntity ent)
        {
            if (ent is not BasePlayer player || player.IsNpc) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try
            {
                DynamicCupShareHarmony.Interface.NextTick(() =>
                {
                    if (player)
                        plugin.OnWorkbenchTriggerChanged(player);
                });
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Workbench trigger leave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.AddPlayer), typeof(BasePlayer), typeof(ulong))]
    internal static class BuildingPrivlidge_AddPlayer_Workbench_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildingPrivlidge __instance, ulong targetPlayerId)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null || !__instance.IsAuthed(targetPlayerId))
                return;

            BasePlayer player = BasePlayer.FindAwakeOrSleepingByID(targetPlayerId);
            if (player == null) return;
            try { plugin.OnBuildingWorkbenchCupboardAuthorized(__instance.buildingID, player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Workbench auth: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.RemovePlayer), typeof(BasePlayer), typeof(ulong))]
    internal static class BuildingPrivlidge_RemovePlayer_Workbench_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildingPrivlidge __instance, ulong targetPlayerId)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null || __instance.IsAuthed(targetPlayerId))
                return;

            BasePlayer player = BasePlayer.FindAwakeOrSleepingByID(targetPlayerId);
            if (player == null) return;
            try { plugin.OnBuildingWorkbenchCupboardDeauthorized(__instance.buildingID, player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Workbench deauth: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.ClearList))]
    internal static class BuildingPrivlidge_ClearList_Workbench_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildingPrivlidge __instance)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return;
            if (__instance.authorizedPlayers != null && __instance.authorizedPlayers.Count > 0)
                return;
            try { plugin.OnBuildingWorkbenchCupboardCleared(__instance.buildingID); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Workbench clear: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(VehiclePrivilege), nameof(VehiclePrivilege.AddPlayer), typeof(BasePlayer))]
    internal static class VehiclePrivilege_AddPlayer_Workbench_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(VehiclePrivilege __instance, BasePlayer player)
        {
            if (__instance is not PlayerBoatPrivilege privilege || player == null) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnBuildingWorkbenchBoatAuthorized(privilege, player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Boat workbench auth: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(VehiclePrivilege), nameof(VehiclePrivilege.AddPlayer), typeof(BasePlayer), typeof(ulong))]
    internal static class VehiclePrivilege_AddPlayerTarget_Workbench_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(VehiclePrivilege __instance, ulong targetPlayerId)
        {
            if (__instance is not PlayerBoatPrivilege privilege) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            BasePlayer player = BasePlayer.FindAwakeOrSleepingByID(targetPlayerId);
            if (player == null) return;
            try { plugin.OnBuildingWorkbenchBoatAuthorized(privilege, player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Boat workbench auth target: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(VehiclePrivilege), "RemoveSelfAuthorize")]
    internal static class VehiclePrivilege_RemoveSelfAuthorize_Workbench_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(VehiclePrivilege __instance, BaseEntity.RPCMessage rpc)
        {
            if (__instance is not PlayerBoatPrivilege privilege || rpc.player == null) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnBuildingWorkbenchBoatDeauthorized(privilege, rpc.player); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Boat workbench deauth: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(VehiclePrivilege), "ClearList")]
    internal static class VehiclePrivilege_ClearList_Workbench_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(VehiclePrivilege __instance)
        {
            if (__instance is not PlayerBoatPrivilege privilege) return;
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.OnBuildingWorkbenchBoatCleared(privilege); }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] Boat workbench clear: " + ex.Message); }
        }
    }
}
