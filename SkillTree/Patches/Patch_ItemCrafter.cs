// OnItemCraft / OnItemCraftFinished / OnItemCraftCancelled
// Uses string-based method names to avoid compile errors when exact signatures differ.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    /// <summary>
    /// OnItemCraft — postfix after the task is queued so Craft_Speed can clone/modify the blueprint
    /// before ServerUpdate calls GetScaledDuration. Mirrors Oxide CallHook("OnItemCraft", ...).
    /// </summary>
    [HarmonyPatch(typeof(ItemCrafter), nameof(ItemCrafter.CraftItem))]
    public static class ItemCrafter_CraftItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemCrafter __instance, BasePlayer owner, Item fromTempBlueprint, bool __result)
        {
            if (!__result || owner == null || __instance == null) return;
            var queue = __instance.queue;
            if (queue == null || queue.Count == 0) return;
            var task = queue.Last?.Value;
            if (task == null) return;
            try { STPlugin.Dispatch_OnItemCraft(task, owner, fromTempBlueprint); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraft: " + ex.Message); }
        }
    }

    /// <summary>
    /// OnItemCraftFinished — postfix on ItemCrafter.FinishCrafting.
    /// FinishCrafting returns void and keeps the crafted Item as a local; Oxide passes that Item.
    /// Capture it via Dup after CreateByItemID so the postfix can forward a real reference.
    /// </summary>
    [HarmonyPatch(typeof(ItemCrafter), "FinishCrafting", new[] { typeof(ItemCraftTask) })]
    public static class ItemCrafter_FinishCrafting_Patch
    {
        [ThreadStatic]
        static Item _craftedItem;

        static void CaptureCraftedItem(Item item) => _craftedItem = item;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var capture = AccessTools.Method(typeof(ItemCrafter_FinishCrafting_Patch), nameof(CaptureCraftedItem));

            for (int i = 0; i < list.Count; i++)
            {
                var ci = list[i];
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) continue;
                if (!(ci.operand is MethodInfo mi) || mi.Name != "CreateByItemID") continue;
                if (mi.DeclaringType != typeof(ItemManager) && mi.DeclaringType?.Name != "ItemManager") continue;

                // CreateByItemID leaves Item on stack → Dup + capture, then original Stloc keeps it.
                list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, capture));
                break;
            }

            return list;
        }

        [HarmonyPostfix]
        public static void Postfix(ItemCrafter __instance, ItemCraftTask task)
        {
            var item = _craftedItem;
            _craftedItem = null;
            if (task == null || __instance == null || item == null) return;
            try { STPlugin.Dispatch_OnItemCraftFinished(task, item, __instance); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraftFinished: " + ex.Message); }
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
