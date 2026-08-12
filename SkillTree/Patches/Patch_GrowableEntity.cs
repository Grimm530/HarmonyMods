// OnGrowableGathered — Oxide fires in GrowableEntity.GiveFruit AFTER ItemManager.Create
// and BEFORE GiveItem, with the live Item. SkillTree mutates item.amount for Harvest_Grown_Yield.
// Prefix on PickFruit with item=null is a no-op (and runs too early / wrong site).
// CanTakeCutting — Prefix on TakeClones (SkillTree grants bonus clones then returns null so vanilla continues).
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(GrowableEntity), "GiveFruit", typeof(BasePlayer), typeof(int), typeof(bool), typeof(bool))]
    public static class GrowableEntity_GiveFruit_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
        {
            return YieldHookInjector.InjectAfterItemCreate(
                instructions,
                originalMethod,
                AccessTools.Method(typeof(GrowableEntity_GiveFruit_Patch), nameof(Hook)),
                includePlayerArg: true);
        }

        /// <summary>Oxide OnGrowableGathered(plant, item, player) — before GiveItem.</summary>
        public static void Hook(GrowableEntity plant, BasePlayer player, Item item)
        {
            if (plant == null || player == null || item == null) return;
            try { STPlugin.Dispatch_OnGrowableGathered(plant, item, player); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnGrowableGathered: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(GrowableEntity), nameof(GrowableEntity.TakeClones))]
    public static class GrowableEntity_TakeClones_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(GrowableEntity __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { STPlugin.Dispatch_CanTakeCutting(player, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] CanTakeCutting: " + ex.Message); }
        }
    }
}
