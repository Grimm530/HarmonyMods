using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(AutoTurret), nameof(AutoTurret.AddSelfAuthorize), typeof(BasePlayer))]
    internal static class AutoTurret_AddSelfAuthorize_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance, BasePlayer player)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                if (plugin.OnTurretAuthorize(__instance, player) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnTurretAuthorize: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(AutoTurret), "ClearList")]
    internal static class AutoTurret_ClearList_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(AutoTurret __instance, BaseEntity.RPCMessage rpc)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                if (plugin.OnTurretClearList(__instance, rpc.player) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnTurretClearList: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.AddPlayer))]
    internal static class BuildingPrivlidge_AddPlayer_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BuildingPrivlidge __instance, BasePlayer granter, ulong targetPlayerId)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null || granter == null) return true;
            try
            {
                if (plugin.OnCupboardAuthorize(__instance, granter) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnCupboardAuthorize: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.ClearList))]
    internal static class BuildingPrivlidge_ClearList_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BuildingPrivlidge __instance, BaseEntity.RPCMessage rpc)
        {
            var plugin = DynamicCupShareMod.Instance?.Plugin;
            if (plugin == null) return true;
            try
            {
                if (plugin.OnCupboardClearList(__instance, rpc.player) != null)
                    return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[DynamicCupShare] OnCupboardClearList: " + ex.Message); }
            return true;
        }
    }
}
