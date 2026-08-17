using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace HarmonyMetrics.HarmonyPatches.Delayed;

[HarmonyPatch]
internal static class ConsoleSystem_Internal_Patch
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    [HarmonyPrepare]
    public static bool Prepare()
    {
        return HarmonyMetricsLoader.ServerStarted;
    }

    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.DeclaredMethod(typeof(ConsoleSystem), "Internal");
        if (method != null)
        {
            yield return method;
        }
    }

    [HarmonyPrefix]
    public static void Prefix(ref long __state)
    {
        __state = Stopwatch.GetTimestamp();
    }

    [HarmonyPostfix]
    public static void Postfix(ConsoleSystem.Arg arg, long __state)
    {
        if (!MetricsLogger.IsReady || arg == null || arg.cmd == null)
        {
            return;
        }

        var ms = (Stopwatch.GetTimestamp() - __state) * TicksToMs;
        MetricsLogger.Instance.ServerConsoleCommands.LogTime(arg.cmd.FullName, ms);
    }
}
