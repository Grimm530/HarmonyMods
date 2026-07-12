using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    [HarmonyPatch(typeof(SurveyCharge), "Explode")]
    internal class SurveyCharge_Explode
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

            var hookMethod = typeof(SurveyCharge_Explode).GetMethod("Hook", BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < list.Count; i++)
            {
                var instr = list[i];
                if ((instr.opcode == OpCodes.Callvirt) && instr.operand is MethodInfo method &&
                    method.Name == "Drop" && method.DeclaringType == typeof(Item))
                {
                    var toInject = new List<CodeInstruction>
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        GetLdloc(itemLocalIndex),
                        new CodeInstruction(OpCodes.Call, hookMethod)
                    };
                    list.InsertRange(i, toInject);
                    return list;
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

        public static void Hook(SurveyCharge surveyCharge, Item item)
        {
            try
            {
                if (GatherManagerMod.Instance == null || item == null) return;
                GatherManagerMod.Instance.ApplySurveyModifier(surveyCharge, item);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
