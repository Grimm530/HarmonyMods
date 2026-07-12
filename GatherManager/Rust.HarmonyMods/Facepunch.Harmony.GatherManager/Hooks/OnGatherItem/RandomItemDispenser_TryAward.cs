using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    /// <summary>Scales random items given by collectibles (e.g. seeds from hemp). Uses pickup modifiers or global scale.</summary>
    [HarmonyPatch(typeof(RandomItemDispenser), "TryAward")]
    internal static class RandomItemDispenser_TryAward
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase originalMethod)
        {
            var list = new List<CodeInstruction>(instructions);
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

            var hookMethod = typeof(RandomItemDispenser_TryAward).GetMethod("Hook", BindingFlags.Public | BindingFlags.Static);
            if (hookMethod == null) return list;

            var toInject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                GetLdloc(itemLocalIndex),
                new CodeInstruction(OpCodes.Call, hookMethod)
            };

            for (int i = 0; i < list.Count; i++)
            {
                var instr = list[i];
                if ((instr.opcode == OpCodes.Call || instr.opcode == OpCodes.Callvirt) && instr.operand is MethodInfo method)
                {
                    if (method.Name != "Create" || method.DeclaringType?.Name != "ItemManager" || method.GetParameters().Length < 3)
                        continue;
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
                    list.InsertRange(insertAt, toInject);
                    return list;
                }
            }
            return list;
        }

        static CodeInstruction GetLdloc(int index)
        {
            return index switch
            {
                0 => new CodeInstruction(OpCodes.Ldloc_0),
                1 => new CodeInstruction(OpCodes.Ldloc_1),
                2 => new CodeInstruction(OpCodes.Ldloc_2),
                3 => new CodeInstruction(OpCodes.Ldloc_3),
                _ => new CodeInstruction(OpCodes.Ldloc_S, index)
            };
        }

        public static void Hook(RandomItemDispenser dispenser, Item item)
        {
            try
            {
                if (item?.info == null || GatherManagerMod.Instance == null) return;
                var scale = GatherManagerMod.Instance.GetResourceModifierForPickup(item.info.displayName.english);
                if (Math.Abs(scale - 1f) < 0.001f) return;
                var amt = item.amount;
                item.amount = Mathf.Max(1, Mathf.FloorToInt(amt * scale));
                if (GatherManagerMod.Instance.DebugGatherEnabled)
                    Debug.Log($"[GatherManager] RandomItemDispenser TryAward Hook: {item.info.displayName.english} scale={scale} before={amt} after={item.amount}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
