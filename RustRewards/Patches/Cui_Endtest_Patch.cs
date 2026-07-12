using System;
using HarmonyLib;
using UnityEngine;

namespace RustRewardsHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). RustRewards CUI buttons are rewritten
    /// to "cui.endtest RR …" in RustCui; this prefix routes them.
    /// Returns true for non-RR payloads so Shop/Kits and other mods still work.
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
            if (!string.Equals(marker, "RR", StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = RustRewardsHarmonyMod.Instance;
            if (mod?.Plugin == null) return true;

            mod.HandleCuiEndtest(args, a);
            return false;
        }
    }
}
