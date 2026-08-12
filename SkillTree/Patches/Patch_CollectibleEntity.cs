// OnCollectiblePickup — Prefix on CollectibleEntity.DoPickup (matches Oxide timing).
// SkillTree mutates entity.itemList amounts before ItemManager.Create / GiveItem.
// Must be Prefix: DoPickup ends with Kill(), and yield must apply before items are created.
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
            if (__instance == null || reciever == null || __instance.itemList == null) return;
            try { STPlugin.Dispatch_OnCollectiblePickup(__instance, reciever); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnCollectiblePickup: " + ex.Message); }
        }
    }
}
