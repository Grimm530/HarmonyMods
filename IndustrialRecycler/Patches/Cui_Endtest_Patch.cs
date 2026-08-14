using System;
using HarmonyLib;
using UnityEngine;

namespace IndustrialRecyclerHarmony.Patches
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
            if (!string.Equals(marker, IndustrialRecyclerMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;
            var mod = IndustrialRecyclerMod.Instance;
            if (mod == null) return true;
            try { mod.HandleCuiEndtest(args, a); }
            catch (Exception ex) { Debug.LogWarning("[IndustrialRecycler] cui.endtest INDUSTRIALRECYCLER: " + ex); }
            return false;
        }
    }
}
