using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HarmonyMetrics;

public class HarmonyMetricsLoader : IHarmonyModHooks
{
    public static bool ServerStarted;
    public static Harmony DelayedHarmony;
    public static readonly List<Harmony> ModTimeWarningHarmonies = new List<Harmony>();
    private static bool _hooksActive;

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        if (_hooksActive)
        {
            return;
        }

        _hooksActive = true;

        if (!Bootstrap.bootstrapInitRun)
        {
            return;
        }

        MetricsLogger.Initialize();

        if (MetricsLogger.Instance != null)
        {
            MetricsLogger.Instance.OnServerStarted();
        }
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        _hooksActive = false;
        ServerStarted = false;
        MetricsLogger.IsReady = false;

        DelayedHarmony?.UnpatchAll(DelayedHarmony.Id);
        DelayedHarmony = null;

        for (var i = 0; i < ModTimeWarningHarmonies.Count; i++)
        {
            var instance = ModTimeWarningHarmonies[i];
            instance?.UnpatchAll(instance.Id);
        }
        ModTimeWarningHarmonies.Clear();

        if (MetricsLogger.Instance != null)
        {
            Object.DestroyImmediate(MetricsLogger.Instance.gameObject);
        }

        NpcCensus.InvalidateTypeCache();
    }

    public void AddModTimeWarnings(List<MethodInfo> methods)
    {
        var instance = new Harmony("HarmonyMetrics.ModTimeWarnings." + ModTimeWarningHarmonies.Count);
        ModTimeWarningHarmonies.Add(instance);

        ModTimeWarnings.Methods.Clear();
        ModTimeWarnings.Methods.AddRange(methods);

        var patchProcessor = new PatchClassProcessor(instance, typeof(ModTimeWarnings));
        patchProcessor.Patch();

        Debug.Log("[HarmonyMetrics]: Added " + methods.Count + " ModTimeWarnings");
    }
}
