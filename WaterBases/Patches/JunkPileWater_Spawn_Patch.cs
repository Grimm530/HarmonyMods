using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace WaterBasesHarmony.Patches
{
    /// <summary>
    /// From WaterBasesJunkpileFix: replace Physics.CheckSphere layermask in JunkPileWater.Spawn
    /// unless the prefab name contains "ghost".
    /// </summary>
    [HarmonyPatch(typeof(JunkPileWater), nameof(JunkPileWater.Spawn))]
    public static class JunkPileWater_Spawn_Patch
    {
        public const int LAYERMASK_VEHICLE_LARGE_CONSTRUCTION_DEPLOYED_DEFAULT =
            1 << (int)Rust.Layer.Vehicle_Large
            | 1 << (int)Rust.Layer.Construction
            | 1 << (int)Rust.Layer.Deployed
            | 1 << (int)Rust.Layer.Default;

        public static int ReplaceOnStackIfNotGhost(JunkPileWater instance, int originalLayermask)
        {
            if (instance != null && instance.PrefabName != null && instance.PrefabName.Contains("ghost"))
                return originalLayermask;

            return LAYERMASK_VEHICLE_LARGE_CONSTRUCTION_DEPLOYED_DEFAULT;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            var replace = AccessTools.Method(typeof(JunkPileWater_Spawn_Patch), nameof(ReplaceOnStackIfNotGhost));

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Call)
                    continue;

                if (!(codes[i].operand is MethodInfo usedMethod))
                    continue;

                if (usedMethod.DeclaringType != typeof(Physics))
                    continue;

                if (usedMethod.Name != nameof(Physics.CheckSphere))
                    continue;

                var localLayerMask = generator.DeclareLocal(typeof(int));
                codes.Insert(i, new CodeInstruction(OpCodes.Stloc, localLayerMask));
                codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(i + 2, new CodeInstruction(OpCodes.Ldloc, localLayerMask));
                codes.Insert(i + 3, new CodeInstruction(OpCodes.Call, replace));
                break;
            }

            return codes;
        }
    }
}
