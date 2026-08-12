// OnDispenserGather / OnDispenserBonus
// Oxide fires these after ItemManager.Create* and BEFORE GiveItem, with the live Item.
// SkillTree.HandleDispenser / OnDispenserBonus mutate item.amount (additive yields) and
// need gatherType / containedItems from the dispenser — so a postfix with item=null is a no-op.
// Mirror GatherManager: transpile a call immediately after the Item is stored to a local.
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using STPlugin = Oxide.Plugins.SkillTree;

namespace SkillTreeHarmony.Patches
{
    [HarmonyPatch(typeof(ResourceDispenser), "GiveResourceFromItem")]
    public static class ResourceDispenser_GiveResourceFromItem_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
        {
            return YieldHookInjector.InjectAfterItemCreate(
                instructions,
                originalMethod,
                AccessTools.Method(typeof(ResourceDispenser_GiveResourceFromItem_Patch), nameof(Hook)),
                includePlayerArg: true);
        }

        /// <summary>Oxide OnDispenserGather(dispenser, player, item) — before GiveItem.</summary>
        public static void Hook(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (dispenser == null || player == null || item == null) return;
            try { STPlugin.Dispatch_OnDispenserGather(dispenser, player, item); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnDispenserGather: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.AssignFinishBonus))]
    public static class ResourceDispenser_AssignFinishBonus_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
        {
            return YieldHookInjector.InjectAfterItemCreate(
                instructions,
                originalMethod,
                AccessTools.Method(typeof(ResourceDispenser_AssignFinishBonus_Patch), nameof(Hook)),
                includePlayerArg: true,
                injectAllCreates: true);
        }

        /// <summary>Oxide OnDispenserBonus(dispenser, player, item) — before GiveItem.</summary>
        public static void Hook(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (dispenser == null || player == null || item == null) return;
            try { STPlugin.Dispatch_OnDispenserBonus(dispenser, player, item); }
            catch (System.Exception ex) { Debug.LogWarning("[SkillTree] OnDispenserBonus: " + ex.Message); }
        }
    }

    /// <summary>Shared IL helper: after ItemManager.Create*/CreateByItemID + stloc, call Hook(this, [player,] item).</summary>
    internal static class YieldHookInjector
    {
        public static IEnumerable<CodeInstruction> InjectAfterItemCreate(
            IEnumerable<CodeInstruction> instructions,
            MethodBase originalMethod,
            MethodInfo hookMethod,
            bool includePlayerArg,
            bool injectAllCreates = false)
        {
            var list = new List<CodeInstruction>(instructions);
            if (hookMethod == null) return list;

            var locals = originalMethod.GetMethodBody()?.LocalVariables;
            if (locals == null) return list;

            int itemLocalIndex = -1;
            for (int i = 0; i < locals.Count; i++)
            {
                if (locals[i].LocalType == typeof(Item))
                {
                    itemLocalIndex = locals[i].LocalIndex;
                    break;
                }
            }
            if (itemLocalIndex < 0) return list;

            var toInject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
            };
            if (includePlayerArg)
                toInject.Add(new CodeInstruction(OpCodes.Ldarg_1));
            toInject.Add(GetLdloc(itemLocalIndex));
            toInject.Add(new CodeInstruction(OpCodes.Call, hookMethod));

            var insertPoints = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                var instr = list[i];
                if ((instr.opcode != OpCodes.Call && instr.opcode != OpCodes.Callvirt) || instr.operand is not MethodInfo method)
                    continue;
                if (method.DeclaringType != typeof(ItemManager))
                    continue;

                bool isCreate = method.Name == "CreateByItemID"
                    || (method.Name == "Create" && method.GetParameters().Length >= 2);
                if (!isCreate) continue;

                int insertAt = i + 1;
                while (insertAt < list.Count)
                {
                    var next = list[insertAt];
                    if (next.opcode == OpCodes.Stloc || next.opcode == OpCodes.Stloc_S || next.opcode == OpCodes.Stloc_0 ||
                        next.opcode == OpCodes.Stloc_1 || next.opcode == OpCodes.Stloc_2 || next.opcode == OpCodes.Stloc_3)
                    {
                        insertAt++;
                        break;
                    }
                    insertAt++;
                }
                insertPoints.Add(insertAt);
                if (!injectAllCreates) break;
            }

            for (int p = insertPoints.Count - 1; p >= 0; p--)
                list.InsertRange(insertPoints[p], toInject);

            return list;
        }

        static CodeInstruction GetLdloc(int index) => index switch
        {
            0 => new CodeInstruction(OpCodes.Ldloc_0),
            1 => new CodeInstruction(OpCodes.Ldloc_1),
            2 => new CodeInstruction(OpCodes.Ldloc_2),
            3 => new CodeInstruction(OpCodes.Ldloc_3),
            _ => new CodeInstruction(OpCodes.Ldloc_S, index)
        };
    }
}
