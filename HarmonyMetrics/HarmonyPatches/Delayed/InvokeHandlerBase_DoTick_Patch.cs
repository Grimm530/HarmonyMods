using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HarmonyMetrics.HarmonyPatches.Utility;

namespace HarmonyMetrics.HarmonyPatches.Delayed;

[HarmonyPatch]
internal static class InvokeHandlerBase_DoTick_Patch
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    private static readonly CodeMatch[] NeedleSequenceToFind =
    {
        CodeMatch.LoadsField(AccessTools.Field(typeof(InvokeAction), nameof(InvokeAction.action))),
        CodeMatch.Calls(AccessTools.Method(typeof(Action), nameof(Action.Invoke)))
    };

    private static readonly CodeInstruction[] SequenceToInject =
    {
        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InvokeHandlerBase_DoTick_Patch), nameof(InvokeWrapper)))
    };

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
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.DeclaredMethod(typeof(InvokeHandlerBase<InvokeHandler>), "DoTick");
        if (method != null)
        {
            yield return method;
        }
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalInstructions)
    {
        var instructionsList = new List<CodeInstruction>(originalInstructions);

        try
        {
            var codeMatcher = new CodeMatcher(instructionsList);
            codeMatcher.MatchStartForward(NeedleSequenceToFind)
                .ThrowIfInvalid("Unable to find the expected injection point")
                .RemoveInstructions(2)
                .InsertAndAdvance(SequenceToInject);

            return codeMatcher.Instructions();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("[HarmonyMetrics] InvokeHandlerBase_DoTick_Patch: " + e.Message);
            return instructionsList;
        }
    }

    private static void InvokeWrapper(InvokeAction invokeAction)
    {
        if (!MetricsLogger.IsReady)
        {
            invokeAction.action.Invoke();
            return;
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            invokeAction.action.Invoke();
        }
        finally
        {
            var logger = MetricsLogger.Instance;
            if (logger != null)
            {
                var ms = (Stopwatch.GetTimestamp() - start) * TicksToMs;
                logger.ServerInvokes.LogTime(invokeAction.action.Method, ms);
            }
        }
    }
}
