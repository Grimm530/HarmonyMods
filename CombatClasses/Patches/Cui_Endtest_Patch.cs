using System;
using HarmonyLib;
using UnityEngine;

namespace CombatClassesHarmony.Patches
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
            if (!string.Equals(marker, CombatClassesMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = CombatClassesMod.Instance;
            if (mod == null) return true;

            try
            {
                mod.HandleCuiEndtest(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CombatClasses] cui.endtest CC: " + ex);
            }

            return false;
        }
    }
}
