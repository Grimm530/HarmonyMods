using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace HarmonyMetrics.HarmonyPatches.Delayed;

internal static class ServerMgr_Metrics_Patches
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    public static readonly HarmonyMethod Prefix = new HarmonyMethod(typeof(ServerMgr_Metrics_Patches), nameof(PrefixImpl));
    public static readonly HarmonyMethod Postfix = new HarmonyMethod(typeof(ServerMgr_Metrics_Patches), nameof(PostfixImpl));

    public static List<MethodBase> CollectTargets()
    {
        var list = new List<MethodBase>();
        TryAdd(list, AccessTools.Method(typeof(ServerMgr), nameof(ServerMgr.Update)));
        TryAdd(list, AccessTools.Method(typeof(ServerBuildingManager), nameof(ServerBuildingManager.Cycle)));
        TryAdd(list, AccessTools.Method(typeof(ServerBuildingManager), "Merge"));
        TryAdd(list, AccessTools.Method(typeof(ServerBuildingManager), "Split"));
        TryAdd(list, AccessTools.Method(typeof(BasePlayer), nameof(BasePlayer.ServerCycle)));
        TryAdd(list, AccessTools.Method(typeof(ConnectionQueue), nameof(ConnectionQueue.Cycle)));
        TryAdd(list, AccessTools.Method(typeof(AIThinkManager), nameof(AIThinkManager.ProcessQueue)));
        TryAdd(list, AccessTools.Method(typeof(IOEntity), nameof(IOEntity.ProcessQueue)));
        TryAdd(list, AccessTools.Method(typeof(BasePet), nameof(BasePet.ProcessMovementQueue)));
        TryAdd(list, AccessTools.Method(typeof(BaseMountable), nameof(BaseMountable.FixedUpdateCycle)));
        TryAdd(list, AccessTools.Method(typeof(Buoyancy), nameof(Buoyancy.Cycle)));
        TryAdd(list, AccessTools.DeclaredMethod(typeof(BaseEntity), nameof(BaseEntity.Spawn)));
        TryAdd(list, AccessTools.DeclaredMethod(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), new[] { typeof(BaseNetworkable.DestroyMode), typeof(bool) }));
        TryAdd(list, AccessTools.DeclaredMethod(typeof(Network.BaseNetwork), nameof(Network.BaseNetwork.Cycle)));
        return list;
    }

    private static void TryAdd(List<MethodBase> list, MethodBase method)
    {
        if (method != null)
        {
            list.Add(method);
        }
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

        var logger = MetricsLogger.Instance;
        if (logger == null)
        {
            return;
        }

        var ms = (Stopwatch.GetTimestamp() - __state) * TicksToMs;
        var name = (__originalMethod.DeclaringType != null ? __originalMethod.DeclaringType.Name : "unknown") + "." + __originalMethod.Name;
        logger.ServerUpdate.LogTime(name, ms);
    }
}
