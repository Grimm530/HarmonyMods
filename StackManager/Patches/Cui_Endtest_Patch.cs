using System;
using HarmonyLib;
using UnityEngine;

namespace StackManagerHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). StackManager CUI buttons are rewritten
    /// to "cui.endtest STACKMANAGER stackmanager.callback …" in ChaosUI.Show; this marker routes them.
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
            if (!string.Equals(marker, "STACKMANAGER", StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = StackManagerHarmonyMod.Instance;
            if (mod?.Plugin == null) return true;

            try
            {
                mod.HandleCuiCallback(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StackManager] cui.endtest STACKMANAGER: " + ex);
            }
            return false;
        }
    }
}
