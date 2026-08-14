using System;
using HarmonyLib;
using UnityEngine;

namespace JetPackHarmony.Patches
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
            if (!string.Equals(marker, JetPackMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;
            var mod = JetPackMod.Instance;
            if (mod == null) return true;
            try { mod.HandleCuiEndtest(args, a); }
            catch (Exception ex) { Debug.LogWarning("[JetPack] cui.endtest JETPACK: " + ex); }
            return false;
        }
    }
}
