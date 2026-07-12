using HarmonyLib;
using UnityEngine;

namespace MapVoter.Patches;

/// <summary>
/// Intercepts cui.endtest when used for MapVoter CUI buttons.
/// Uses "cui.endtest MapVoter_XXX" (no SENDCMD) so TCUpgrade doesn't intercept - it only handles SENDCMD.
/// </summary>
[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Cui_Endtest_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg args)
    {
        var a = args?.Args;
        if (a == null || a.Length < 1) return true;

        string first = a[0].ToString();
        if (string.IsNullOrEmpty(first) || !first.StartsWith("MapVoter")) return true;

        var mod = MapVoterMod.Instance;
        if (mod == null) return true;

        var player = args.Player();
        mod.HandleCuiCommand(player, System.Array.ConvertAll(a, x => x.ToString()));
        return false;
    }
}
