// OnEntitySpawned (server spawn) and OnEntityBuilt (deploy/build).
using HarmonyLib;
using UnityEngine;
using TPVE = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class Patch_BaseNetworkable_Spawn
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance == null || __instance is BasePlayer) return;
            try { TPVE.Dispatch_OnEntitySpawned(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntitySpawned: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Patch_Planner_DoBuild
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__instance == null || __result == null) return;
            try { TPVE.Dispatch_OnEntityBuilt(__instance, __result.gameObject); }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntityBuilt: " + ex.Message); }
        }
    }

    /// <summary>
    /// OnConstructionPlace (TruePVE 2.4.3 planter planting). Oxide injects this inside
    /// Planner.DoPlacement after the entity exists but before parent is assigned, so
    /// CanHarvest uses placement.entity as the planter parent.
    /// Non-null dispatch result kills the planted growable (same as Oxide KillMessage).
    /// </summary>
    [HarmonyPatch(typeof(Planner), nameof(Planner.DoPlacement))]
    public static class Patch_Planner_DoPlacement
    {
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, Construction.Target placement, Construction component, GameObject __result)
        {
            if (__instance == null || __result == null) return;
            GrowableEntity plant = __result.GetComponent<GrowableEntity>();
            if (plant == null) return;
            BasePlayer player = __instance.GetOwnerPlayer();
            if (player == null) return;
            try
            {
                if (TPVE.Dispatch_OnConstructionPlace(plant, component, placement, player) != null && plant.IsValid())
                    plant.KillMessage();
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnConstructionPlace: " + ex.Message); }
        }
    }
}
