using System;
using HarmonyLib;
using UnityEngine;

namespace AirbourneSpawnHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands. Beach button uses
    /// "cui.endtest AIRBOURNESPAWN beach".
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;

            string marker = a[0].ToString();
            if (!string.Equals(marker, "AIRBOURNESPAWN", StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = AirbourneSpawnMod.Instance;
            if (mod?.Plugin == null) return true;

            try
            {
                mod.HandleCuiCallback(args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AirbourneSpawn] cui.endtest AIRBOURNESPAWN: " + ex);
            }
            return false;
        }
    }
}
