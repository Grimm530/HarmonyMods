using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HarmonyMetrics.HarmonyPatches.Delayed;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace HarmonyMetrics;

internal static class DelayedPatchApplicator
{
    public static IEnumerator Apply(MetricsLogger logger, Harmony harmony, Config.ConfigData config)
    {
        var sw = Stopwatch.StartNew();

        yield return ApplyClass(harmony, typeof(ConsoleSystem_Internal_Patch));
        yield return null;
        yield return ApplyClass(harmony, typeof(InvokeHandlerBase_DoTick_Patch));
        yield return null;

        Debug.Log("[HarmonyMetrics]: Applying server_update timing patches...");
        yield return ApplyMethodsSpread(harmony, ServerMgr_Metrics_Patches.CollectTargets(), ServerMgr_Metrics_Patches.Prefix, ServerMgr_Metrics_Patches.Postfix, 4);
        yield return null;

        if (config != null && config.GatherWorkQueueTiming)
        {
            Debug.Log("[HarmonyMetrics]: Applying work-queue timing patches (spread across frames)...");
            yield return ApplyMethodsSpread(harmony, ObjectWorkQueue_RunJob_Patch.CollectTargets(), ObjectWorkQueue_RunJob_Patch.Prefix, ObjectWorkQueue_RunJob_Patch.Postfix, 8);
        }

        if (config != null && config.GatherRpcTiming)
        {
            Debug.Log("[HarmonyMetrics]: Applying RPC timing patches (spread across frames)...");
            yield return ApplyMethodsSpread(harmony, RPCServer_Attribute_Method_Patch.CollectTargets(), RPCServer_Attribute_Method_Patch.Prefix, RPCServer_Attribute_Method_Patch.Postfix, 12);
        }

        sw.Stop();
        Debug.Log("[HarmonyMetrics]: Startup patches finished in " + sw.ElapsedMilliseconds + "ms wall time (spread across frames)");
    }

    private static IEnumerator ApplyClass(Harmony harmony, System.Type patchClass)
    {
        try
        {
            var processor = new PatchClassProcessor(harmony, patchClass);
            var applied = processor.Patch();
            Debug.Log(applied == null
                ? "[HarmonyMetrics]: Failed to apply patch: " + patchClass.Name
                : "[HarmonyMetrics]: Applied startup patch: " + patchClass.Name);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[HarmonyMetrics]: Failed to apply " + patchClass.Name + ": " + ex.Message);
        }

        yield break;
    }

    private static IEnumerator ApplyMethodsSpread(
        Harmony harmony,
        List<MethodBase> methods,
        HarmonyMethod prefix,
        HarmonyMethod postfix,
        int perFrame)
    {
        if (methods == null || methods.Count == 0)
        {
            yield break;
        }

        var patched = 0;
        for (var i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            if (method == null)
            {
                continue;
            }

            try
            {
                harmony.Patch(method, prefix: prefix, postfix: postfix);
                patched++;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[HarmonyMetrics]: Skip patch " + method.DeclaringType?.Name + "." + method.Name + ": " + ex.Message);
            }

            if (perFrame > 0 && patched % perFrame == 0)
            {
                yield return null;
            }
        }

        Debug.Log("[HarmonyMetrics]: Patched " + patched + " methods");
    }
}
