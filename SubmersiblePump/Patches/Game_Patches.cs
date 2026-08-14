using HarmonyLib;
using UnityEngine;

namespace SubmersiblePump.Patches
{
    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    internal static class Planner_DoBuild_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Planner __instance, BaseEntity __result)
        {
            var plugin = SubmersiblePumpMod.Instance?.Plugin;
            if (plugin == null || __instance == null || __result == null) return;
            try { plugin.OnEntityBuilt(__instance, __result.gameObject); }
            catch (System.Exception ex) { Debug.LogWarning("[SubmersiblePump] OnEntityBuilt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable __instance)
        {
            var plugin = SubmersiblePumpMod.Instance?.Plugin;
            if (plugin == null) return;
            var pump = __instance as WaterPump;
            if (pump == null) return;
            try { plugin.OnEntityKill(pump); }
            catch (System.Exception ex) { Debug.LogWarning("[SubmersiblePump] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), "CanCompletePickup")]
    internal static class CanCompletePickup_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseCombatEntity __instance, BasePlayer player, ref bool __result)
        {
            var plugin = SubmersiblePumpMod.Instance?.Plugin;
            if (plugin == null) return true;
            var pump = __instance as WaterPump;
            if (pump == null) return true;
            try
            {
                object result = plugin.CanPickupEntity(player, pump);
                if (result is bool b)
                {
                    __result = b;
                    return false;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[SubmersiblePump] CanPickupEntity: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Hammer), nameof(Hammer.DoAttackShared))]
    internal static class Hammer_DoAttackShared_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Hammer __instance, HitInfo info)
        {
            var plugin = SubmersiblePumpMod.Instance?.Plugin;
            if (plugin == null || info == null) return;
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return;
            try { plugin.OnHammerHit(player, info); }
            catch (System.Exception ex) { Debug.LogWarning("[SubmersiblePump] OnHammerHit: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.DoAutomatedSave), typeof(bool))]
    internal static class ServerSave_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            var plugin = SubmersiblePumpMod.Instance?.Plugin;
            if (plugin == null) return;
            try { plugin.SaveData(); }
            catch (System.Exception ex) { Debug.LogWarning("[SubmersiblePump] Save: " + ex.Message); }
        }
    }
}
