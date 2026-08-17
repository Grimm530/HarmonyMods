using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HarmonyMetrics.HarmonyPatches.Utility;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace HarmonyMetrics;

[HarmonyPatch]
public static class ModTimeWarnings
{
    public static readonly List<MethodInfo> Methods = new List<MethodInfo>();

    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    [HarmonyPrepare]
    public static bool Prepare()
    {
        if (!HarmonyMetricsLoader.ServerStarted)
        {
            return false;
        }

        return true;
    }

    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods(Harmony harmonyInstance)
    {
        return Methods;
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions, MethodBase methodBase, ILGenerator ilGenerator)
    {
        var ret = new List<CodeInstruction>(originalInstructions);
        var local = ilGenerator.DeclareLocal(typeof(long));

        ret.InsertRange(0, new CodeInstruction[]
        {
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Stopwatch), nameof(Stopwatch.GetTimestamp))),
            new CodeInstruction(OpCodes.Stloc, local)
        });

        return Helpers.Postfix(ret,
            new System.Action<string, long>(CustomPostfix),
            new CodeInstruction(OpCodes.Ldstr, methodBase.DeclaringType?.Name + "." + methodBase.Name),
            new CodeInstruction(OpCodes.Ldloc, local));
    }

    private static void CustomPostfix(string methodName, long __state)
    {
        if (!MetricsLogger.IsReady)
        {
            return;
        }

        var ms = (Stopwatch.GetTimestamp() - __state) * TicksToMs;
        MetricsLogger.Instance.TimeWarnings.LogTime(methodName, ms);
    }
}
