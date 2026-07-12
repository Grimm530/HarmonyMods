// OnDispenserGather / OnDispenserBonus
// GiveResourceFromItem -> OnDispenserGather (postfix, item=null acceptable for SkillTree)
// AssignFinishBonus    -> OnDispenserBonus  (postfix)
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.GiveResourceFromItem))]
    public static class ResourceDispenser_GiveResourceFromItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResourceDispenser __instance, BasePlayer entity)
        {
            if (__instance == null || entity == null) return;
            try { STPlugin.Dispatch_OnDispenserGather(__instance, entity, null); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnDispenserGather: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.AssignFinishBonus))]
    public static class ResourceDispenser_AssignFinishBonus_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResourceDispenser __instance, BasePlayer player)
        {
            if (__instance == null || player == null) return;
            try { STPlugin.Dispatch_OnDispenserBonus(__instance, player, null); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnDispenserBonus: " + ex.Message); }
        }
    }
}
