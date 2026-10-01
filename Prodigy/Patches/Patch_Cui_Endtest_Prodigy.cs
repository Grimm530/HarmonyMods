using HarmonyLib;

namespace Prodigy.Patches;

/// <summary>
/// Intercepts cui.endtest when used for Prodigy UI (arrows and close).
/// Buttons use "cui.endtest PRODIGY close" or "cui.endtest PRODIGY up &lt;encoded&gt;" so the client sends to server.
/// </summary>
[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Patch_Cui_Endtest_Prodigy
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg args)
    {
        var a = args?.Args;
        if (a == null || a.Length < 2 || a[0] != "PRODIGY")
            return true;

        var mod = ProdigyMod.Instance;
        if (mod == null) return true;

        var player = args.Connection?.player as BasePlayer;
        if (player == null) return true;

        string direction = a[1].ToString();
        string encodedArg = a.Length >= 3 ? a[2].ToString() : null;
        mod.RunProdigyUiMove(player, direction, encodedArg);
        return false;
    }
}