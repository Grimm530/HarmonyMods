using System;
using HarmonyLib;
using UnityEngine;

namespace AutoCodeLockHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands. AutoCodeLock CUI buttons are rewritten
    /// to "cui.endtest AUTOCODELOCK autocodelock.callback …" in ChaosUI.Show.
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
            if (!string.Equals(marker, "AUTOCODELOCK", StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = AutoCodeLockMod.Instance;
            if (mod?.Plugin == null) return true;

            try
            {
                mod.HandleCuiCallback(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] cui.endtest AUTOCODELOCK: " + ex);
            }
            return false;
        }
    }
}
