using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AlwaysBonus.Patches;

/// <summary>
/// Transpiler patch for OreResourceEntity.OnAttacked.
/// Replaces the node star hotspot radius multiplier (1.5f) with a runtime-configurable value.
/// When NodeX is enabled: 25f (effectively always hit the star). Otherwise: 1.5f (vanilla).
/// </summary>
[HarmonyPatch(typeof(OreResourceEntity), nameof(OreResourceEntity.OnAttacked), new Type[] { typeof(HitInfo) })]
public class OreResourceEntity_OnAttacked_Patch
{
    /// <summary>
    /// Called at runtime from patched IL. Returns 25f when node star farming is enabled, else 1.5f.
    /// </summary>
    public static float GetNodeRadiusMultiplier()
    {
        AlwaysBonusConfig.LoadConfig();
        return (AlwaysBonusConfig.Config?.NodeX ?? true) ? 25f : 1.5f;
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var method = AccessTools.Method(typeof(OreResourceEntity_OnAttacked_Patch), nameof(GetNodeRadiusMultiplier));
        if (method == null)
            return instructions;

        var list = new List<CodeInstruction>(instructions);
        for (int i = 0; i < list.Count; i++)
        {
            var instr = list[i];
            if (instr.opcode == OpCodes.Ldc_R4 && instr.operand is float f && Math.Abs(f - 1.5f) < 0.001f)
            {
                list[i] = new CodeInstruction(OpCodes.Call, method);
                return list;
            }
        }
        return list;
    }
}
