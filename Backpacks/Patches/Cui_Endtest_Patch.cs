using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace BackpacksHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). Backpacks CUI buttons are
    /// rewritten to "cui.endtest BP …" in RustCui; this marker routes them to registered handlers.
    /// Returns true for non-BP payloads so Shop/Kits/SkillTree/etc. still work.
    /// </summary>
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        public const string Marker = "BP";

        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;

            string marker = a[0].ToString();
            if (!string.Equals(marker, Marker, StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = BackpacksHarmonyMod.Instance;
            if (mod == null) return true;

            try
            {
                mod.HandleCuiEndtest(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Backpacks] cui.endtest BP: " + ex);
            }

            return false;
        }
    }
}
