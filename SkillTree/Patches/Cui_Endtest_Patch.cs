using System;
using HarmonyLib;
using UnityEngine;

namespace SkillTreeHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands (e.g. cui.endtest). SkillTree CUI buttons are rewritten
    /// to "cui.endtest ST …" in RustCui; this marker routes them to registered console handlers.
    /// Returns true for non-ST payloads so Shop/Kits/RaidableBases/etc. still work.
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
            if (!string.Equals(marker, SkillTreeMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = SkillTreeMod.Instance;
            if (mod == null) return true;

            try
            {
                mod.HandleCuiEndtest(args, a);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillTree] cui.endtest ST: " + ex);
            }

            return false;
        }
    }
}
