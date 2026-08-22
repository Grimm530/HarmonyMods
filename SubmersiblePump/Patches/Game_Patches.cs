using Facepunch.Rust;
using HarmonyLib;
using UnityEngine;

namespace SubmersiblePump.Patches
{
    internal struct PlannerItemState
    {
        public ulong Skin;
        public string Name;
    }

    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    internal static class Planner_DoBuild_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Planner __instance, out PlannerItemState __state)
        {
            __state = default;
            Item item = __instance?.GetOwnerItem();
            if (item == null) return;
            __state.Skin = item.skin;
            __state.Name = item.name;
        }

        [HarmonyPostfix]
        private static void Postfix(Planner __instance, BaseEntity __result, PlannerItemState __state)
        {
            if (__result == null) return;
            if (__state.Skin == SubmersiblePumpPlugin.SubmersiblePumpSkin ||
                __state.Skin == SubmersiblePumpPlugin.SubmersiblePumpSkin + 1)
                __result.skinID = __state.Skin;
            else if (__result.skinID == 0 && __state.Skin != 0)
                __result.skinID = __state.Skin;

            var plugin = SubmersiblePumpMod.Instance?.Plugin;
            if (plugin == null) return;
            try
            {
                plugin.TryConvertPlacedGenerator(
                    __result,
                    __instance != null ? __instance.GetOwnerPlayer() : null,
                    __state.Skin,
                    __state.Name);
            }
            catch (System.Exception ex) { Debug.LogWarning("[SubmersiblePump] OnEntityBuilt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Analytics.Azure), nameof(Analytics.Azure.OnEntityBuilt))]
    internal static class Analytics_OnEntityBuilt_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseEntity entity, BasePlayer player)
        {
            var plugin = SubmersiblePumpMod.Instance?.Plugin;
            if (plugin == null || entity == null) return;
            try
            {
                ulong skin = 0UL;
                string name = null;
                Item item = player != null ? player.GetActiveItem() : null;
                if (item != null)
                {
                    skin = item.skin;
                    name = item.name;
                }
                plugin.TryConvertPlacedGenerator(entity, player, skin, name);
            }
            catch (System.Exception ex) { Debug.LogWarning("[SubmersiblePump] OnEntityBuilt(Analytics): " + ex.Message); }
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
