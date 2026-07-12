// OnGrowableGathered — prefix on GrowableEntity.PickFruit.
// Prefix: PickFruit may destroy the entity on final harvest, so postfix may see a dead object.
// CanTakeCutting — prefix on GrowableEntity.TryCutting (if the method exists).
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(GrowableEntity), nameof(GrowableEntity.PickFruit), typeof(BasePlayer), typeof(bool))]
    public static class GrowableEntity_PickFruit_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(GrowableEntity __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { STPlugin.Dispatch_OnGrowableGathered(__instance, null, player); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnGrowableGathered: " + ex.Message); }
        }
    }
}
