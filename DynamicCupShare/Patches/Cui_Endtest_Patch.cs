using System;
using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;

            string marker = a[0].ToString();
            if (!string.Equals(marker, "DYNAMICCUPSHARE", StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = DynamicCupShareMod.Instance;
            if (mod?.Plugin == null) return true;

            try
            {
                mod.HandleCuiCallback(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] cui.endtest DYNAMICCUPSHARE: " + ex);
            }
            return false;
        }
    }
}
