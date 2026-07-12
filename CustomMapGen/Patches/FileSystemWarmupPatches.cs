using System;
using System.Collections;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace CustomMapGen.Patches
{
    /// <summary>
    /// Skip asset warmup on server start when SkipAssetWarmup is enabled (QoL from HarmonyCustomGenerator).
    /// </summary>
    [HarmonyPatch(typeof(FileSystem_Warmup), nameof(FileSystem_Warmup.Run), typeof(Action<string>), typeof(string), typeof(CancellationToken))]
    public static class FileSystemWarmup_Run_Patch
    {
        static bool Prefix(Action<string> statusFunction, string format, CancellationToken ct, ref IEnumerator __result)
        {
            if (!CustomMapGen.IsCustomMapGenEnabled())
                return true;
            var config = CustomMapGen.Instance?.GetConfig();
            if (config == null || !config.SkipAssetWarmup)
                return true;
            UnityEngine.Debug.Log("[CustomMapGen] Skipping asset warmup (SkipAssetWarmup = true)");
            __result = EmptyEnumerator();
            return false;
        }

        private static IEnumerator EmptyEnumerator()
        {
            yield break;
        }
    }
}
