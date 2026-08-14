using System;
using HarmonyLib;
using UnityEngine;

namespace MinimapHarmony.Patches
{
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;

            string marker = a[0].ToString();
            if (!string.Equals(marker, "MINIMAP", StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = MinimapHarmonyMod.Instance;
            if (mod?.Plugin == null) return true;

            try
            {
                mod.HandleCuiCallback(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] cui.endtest MINIMAP: " + ex);
            }
            return false;
        }
    }
}
