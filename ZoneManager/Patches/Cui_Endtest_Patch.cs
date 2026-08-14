using System;
using HarmonyLib;
using UnityEngine;

namespace ZoneManagerHarmony.Patches
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
            if (!string.Equals(marker, ZoneManagerMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = ZoneManagerMod.Instance;
            if (mod == null) return true;

            try { mod.HandleCuiEndtest(args, a); }
            catch (Exception ex) { Debug.LogWarning("[ZoneManager] cui.endtest ZONEMANAGER: " + ex); }
            return false;
        }
    }
}
