// OnCollectiblePickup — prefix on CollectibleEntity.DoPickup.
// Must be Prefix because DoPickup ends with Kill(), so a postfix sees a dead entity.
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(CollectibleEntity), nameof(CollectibleEntity.DoPickup), typeof(BasePlayer), typeof(bool))]
    public static class CollectibleEntity_DoPickup_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(CollectibleEntity __instance, BasePlayer reciever)
        {
            if (__instance == null || reciever == null) return;
            try { STPlugin.Dispatch_OnCollectiblePickup(__instance, reciever); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnCollectiblePickup: " + ex.Message); }
        }
    }
}
