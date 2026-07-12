using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    [HarmonyPatch(typeof(ExcavatorArm), "ProduceResources")]
    internal class ExcavatorArm_ProduceResources
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

            var hookMethod = typeof(ExcavatorArm_ProduceResources).GetMethod("Hook", BindingFlags.Public | BindingFlags.Static);
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
                    if (method.Name == "OnExcavatorProduceItem" && method.DeclaringType?.Name == "Azure")
                    {
                        list.InsertRange(i + 1, toInject);
                        return list;
                    }
                    if (method.Name == "AddItem" || method.Name == "Insert" || method.Name == "GiveItem")
                    {
                        list.InsertRange(i, toInject);
                        return list;
                    }
                }
            }
            return list;
        }

        static CodeInstruction GetLdloc(int index)
        {
            switch (index)
            {
                case 0: return new CodeInstruction(OpCodes.Ldloc_0);
                case 1: return new CodeInstruction(OpCodes.Ldloc_1);
                case 2: return new CodeInstruction(OpCodes.Ldloc_2);
                case 3: return new CodeInstruction(OpCodes.Ldloc_3);
                default: return new CodeInstruction(OpCodes.Ldloc_S, index);
            }
        }

        public static void Hook(ExcavatorArm excavator, Item item)
        {
            try
            {
                if (GatherManagerMod.Instance == null || item == null) return;
                GatherManagerMod.Instance.ApplyExcavatorModifier(excavator, item);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
