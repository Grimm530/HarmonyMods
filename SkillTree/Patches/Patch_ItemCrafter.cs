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
    /// OnItemCraftFinished must run at the Oxide CallHook site — after amountToCreate is applied,
    /// before GiveItem. A FinishCrafting postfix is too late: stacking zeros item.amount and
    /// Remove()s the Item, so Craft_Duplicate calls ItemManager.Create with amount 0
    /// ("Creating item with less than 1 amount!").
    /// </summary>
    [HarmonyPatch(typeof(ItemCrafter), "FinishCrafting", new[] { typeof(ItemCraftTask) })]
    public static class ItemCrafter_FinishCrafting_Patch
    {
        [ThreadStatic]
        static Item _craftedItem;

        static void CaptureCraftedItem(Item item) => _craftedItem = item;

        static void FireCraftFinished(ItemCrafter crafter, ItemCraftTask task)
        {
            var item = _craftedItem;
            _craftedItem = null;
            if (task == null || crafter == null || item == null) return;
            try { STPlugin.Dispatch_OnItemCraftFinished(task, item, crafter); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraftFinished: " + ex.Message); }
        }

        // Oxide: Interface.CallHook("OnItemCraftFinished", task, item, this)
        public static object CallHookShim(string hook, object task, object item, object crafter)
        {
            try { STPlugin.Dispatch_OnItemCraftFinished(task as ItemCraftTask, item as Item, crafter as ItemCrafter); }
            catch (Exception ex) { Debug.LogWarning("[SkillTree] OnItemCraftFinished: " + ex.Message); }
            return null;
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var shim = AccessTools.Method(typeof(ItemCrafter_FinishCrafting_Patch), nameof(CallHookShim));
            var list = CallHookReplace.Replace(instructions, "OnItemCraftFinished", shim, warn: false);

            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) &&
                    list[i].operand is MethodInfo replaced && replaced == shim)
                    return list;
            }

            // Vanilla Assembly-CSharp has no CallHook — fire immediately before GiveItem.
            var capture = AccessTools.Method(typeof(ItemCrafter_FinishCrafting_Patch), nameof(CaptureCraftedItem));
            var fire = AccessTools.Method(typeof(ItemCrafter_FinishCrafting_Patch), nameof(FireCraftFinished));
            bool captured = false;
            for (int i = 0; i < list.Count; i++)
            {
                var ci = list[i];
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) continue;
                if (!(ci.operand is MethodInfo mi)) continue;

                if (!captured && mi.Name == "CreateByItemID" &&
                    (mi.DeclaringType == typeof(ItemManager) || mi.DeclaringType?.Name == "ItemManager"))
                {
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Call, capture));
                    captured = true;
                    i += 2;
                    continue;
                }

                if (mi.Name == "GiveItem" &&
                    (mi.DeclaringType == typeof(PlayerInventory) || mi.DeclaringType?.Name == "PlayerInventory"))
                {
                    list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Call, fire));
                    return list;
                }
            }

            Debug.LogWarning("[SkillTree] CallHookReplace: did not find 'OnItemCraftFinished' — perk may stay dead until Rust IL is re-checked.");
            return list;
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

    /// <summary>
    /// ItemManager.Create logs an untagged Facepunch error when amount &lt;= 0.
    /// If SkillTree is on the stack, emit our tagged line and skip the vanilla log
    /// so the console attributes it. Amount &gt; 0 is a no-op (hot path).
    /// CombatClasses observes this method with a postfix; we only skip the original
    /// on this rare error path (vanilla would return null anyway).
    /// </summary>
    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.Create), new[] { typeof(ItemDefinition), typeof(int), typeof(ulong), typeof(bool), typeof(ulong) })]
    [HarmonyPriority(Priority.First)]
    public static class ItemManager_Create_SkillTreeTag_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ItemDefinition template, int iAmount, ref Item __result)
        {
            if (iAmount > 0) return true;
            if (template == null) return true;
            if (!IsSkillTreeCaller()) return true;

            var name = template.displayName != null ? template.displayName.english : template.shortname;
            Debug.LogError("[SkillTree] Creating item with less than 1 amount! (" + name + ")");
            __result = null;
            return false;
        }

        static bool IsSkillTreeCaller()
        {
            var trace = new System.Diagnostics.StackTrace(2, false);
            int count = trace.FrameCount;
            for (int i = 0; i < count; i++)
            {
                var type = trace.GetFrame(i)?.GetMethod()?.DeclaringType;
                while (type != null)
                {
                    if (type == typeof(STPlugin)) return true;
                    var ns = type.Namespace;
                    if (ns != null && ns.StartsWith("SkillTreeHarmony", StringComparison.Ordinal)) return true;
                    type = type.DeclaringType;
                }
            }
            return false;
        }
    }
}
