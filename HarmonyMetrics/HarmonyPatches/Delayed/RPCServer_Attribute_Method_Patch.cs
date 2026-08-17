using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace HarmonyMetrics.HarmonyPatches.Delayed;

/// <summary>
/// Optional heavy patch set. Off by default — enabling scans every [RPC_Server] method in Assembly-CSharp.
/// Applied via <see cref="DelayedPatchApplicator"/> in small batches so a live reload does not hitch.
/// </summary>
internal static class RPCServer_Attribute_Method_Patch
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
    public static readonly HarmonyMethod Prefix = new HarmonyMethod(typeof(RPCServer_Attribute_Method_Patch), nameof(PrefixImpl));
    public static readonly HarmonyMethod Postfix = new HarmonyMethod(typeof(RPCServer_Attribute_Method_Patch), nameof(PostfixImpl));

    public static List<MethodBase> CollectTargets()
    {
        var results = new List<MethodBase>();
        var baseNetworkableAssembly = typeof(BaseNetworkable).Assembly;
        var typesToScan = new Stack<System.Type>(baseNetworkableAssembly.GetTypes());

        while (typesToScan.Count > 0)
        {
            var type = typesToScan.Pop();
            var nested = type.GetNestedTypes();
            for (var i = 0; i < nested.Length; i++)
            {
                typesToScan.Push(nested[i]);
            }

            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (method.GetCustomAttribute<BaseEntity.RPC_Server>() != null)
                {
                    results.Add(method);
                }
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
        MetricsLogger.Instance.ServerRpcCalls.LogTime(name, ms);
    }
}
