using System;
using HarmonyLib;
using UnityEngine;

namespace LootQoLHarmony.Patches
{
    /// <summary>
    /// Clients only forward ConsoleGen commands. FastLoot / SortButton use
    /// "cui.endtest LOOTQOL take|sort|order".
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
            if (!string.Equals(marker, "LOOTQOL", StringComparison.OrdinalIgnoreCase))
                return true;

            var mod = LootQoLMod.Instance;
            if (mod == null) return true;

            try { mod.HandleCuiCallback(args); }
            catch (Exception ex) { Debug.LogWarning("[LootQoL] cui.endtest LOOTQOL: " + ex); }
            return false;
        }
    }
}
