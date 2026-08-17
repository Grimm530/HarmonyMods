using HarmonyLib;

namespace HarmonyMetrics.HarmonyPatches;

[HarmonyPatch(typeof(Performance), "FPSTimer")]
public static class Performance_FPSTimer_Patch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!MetricsLogger.IsReady)
        {
            return;
        }

        var logger = MetricsLogger.Instance;
        if (logger != null)
        {
            logger.OnPerformanceReportGenerated();
        }
    }
}

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.OpenConnection))]
public static class ServerMgr_OpenConnection_Patch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        var logger = SingletonComponent<MetricsLogger>.Instance;
        if (logger != null)
        {
            logger.OnServerStarted();
        }
    }
}

[HarmonyPatch(typeof(Bootstrap), nameof(Bootstrap.StartServer))]
public static class Bootstrap_StartServer_Patch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        MetricsLogger.Initialize();
    }
}

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
public static class BasePlayer_PlayerInit_Patch
{
    [HarmonyPostfix]
    public static void Postfix(BasePlayer __instance)
    {
        var logger = SingletonComponent<MetricsLogger>.Instance;
        if (logger != null)
        {
            logger.OnPlayerInit(__instance);
        }
    }
}

[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
public static class BasePlayer_OnDisconnected_Patch
{
    [HarmonyPrefix]
    public static void Prefix(BasePlayer __instance)
    {
        var logger = SingletonComponent<MetricsLogger>.Instance;
        if (logger != null)
        {
            logger.OnPlayerDisconnected(__instance);
        }
    }
}

[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.Update))]
public static class ServerMgr_Update_RuntimeProfiler_Patch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        var logger = SingletonComponent<MetricsLogger>.Instance;
        if (logger != null)
        {
            logger.AccumulateRuntimeProfiler();
        }
    }
}
