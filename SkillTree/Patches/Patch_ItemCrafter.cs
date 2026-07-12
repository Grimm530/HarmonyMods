// OnItemCraft / OnItemCraftFinished / OnItemCraftCancelled
// Uses string-based method names to avoid compile errors when exact signatures differ.
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    /// <summary>OnItemCraftFinished — postfix on ItemCrafter.FinishCrafting.</summary>
    [HarmonyPatch(typeof(ItemCrafter), "FinishCrafting", new[] { typeof(ItemCraftTask) })]
    public static class ItemCrafter_FinishCrafting_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemCrafter __instance, ItemCraftTask task)
        {
            if (task == null) return;
            try { STPlugin.Dispatch_OnItemCraftFinished(task, null, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraftFinished: " + ex.Message); }
        }
    }

    /// <summary>
    /// OnItemCraftCancelled — Facepunch changed CancelBlueprint(ItemCraftTask) to CancelBlueprint(int itemid).
    /// CancelTask(int taskUID) is the shared cancel path; capture the task from the queue in Prefix.
    /// </summary>
    [HarmonyPatch(typeof(ItemCrafter), "CancelTask", new[] { typeof(int) })]
    public static class ItemCrafter_CancelTask_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemCrafter __instance, int iID, ref ItemCraftTask __state)
        {
            __state = null;
            var queue = __instance?.queue;
            if (queue == null) return;

            LinkedListNode<ItemCraftTask> node = queue.First;
            while (node != null)
            {
                var task = node.Value;
                if (task != null && task.taskUID == iID)
                {
                    __state = task;
                    return;
                }
                node = node.Next;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ItemCraftTask __state)
        {
            if (__state == null) return;
            try { STPlugin.Dispatch_OnItemCraftCancelled(__state); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraftCancelled: " + ex.Message); }
        }
    }
}
