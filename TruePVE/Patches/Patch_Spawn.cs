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
}
