using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace HarmonyMetrics.HarmonyPatches.Delayed;

/// <summary>
/// Optional heavy patch set. Off by default — enabling scans Assembly-CSharp for every ObjectWorkQueue.RunJob.
/// Applied via <see cref="DelayedPatchApplicator"/> in small batches so a live reload does not hitch.
/// </summary>
internal static class ObjectWorkQueue_RunJob_Patch
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
    public static readonly HarmonyMethod Prefix = new HarmonyMethod(typeof(ObjectWorkQueue_RunJob_Patch), nameof(PrefixImpl));
    public static readonly HarmonyMethod Postfix = new HarmonyMethod(typeof(ObjectWorkQueue_RunJob_Patch), nameof(PostfixImpl));

    public static List<MethodBase> CollectTargets()
    {
        var results = new List<MethodBase>();
        var assemblyCSharp = typeof(BaseNetworkable).Assembly;
        var typesToScan = new Stack<Type>(assemblyCSharp.GetTypes());
        var yielded = new HashSet<string>();

        while (typesToScan.Count > 0)
        {
            var type = typesToScan.Pop();
            var nested = type.GetNestedTypes();
            for (var i = 0; i < nested.Length; i++)
            {
                typesToScan.Push(nested[i]);
            }

            if (type.BaseType == null || type.BaseType.Name.IndexOf("ObjectWorkQueue", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var method = AccessTools.Method(type, "RunJob");
            if (method != null && yielded.Add(type.FullName))
            {
                results.Add(method);
            }
        }

        return results;
    }

    private static void PrefixImpl(ref long __state)
    {
        __state = Stopwatch.GetTimestamp();
    }

    private static void PostfixImpl(MethodBase __originalMethod, long __state)
    {
        if (!MetricsLogger.IsReady || __originalMethod == null)
        {
            return;
        }

        var ms = (Stopwatch.GetTimestamp() - __state) * TicksToMs;
        var name = (__originalMethod.DeclaringType != null ? __originalMethod.DeclaringType.Name : "unknown") + "." + __originalMethod.Name;
        MetricsLogger.Instance.WorkQueueTimes.LogTime(name, ms);
    }
}
