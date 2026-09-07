using HarmonyLib;
using Rust;

namespace RaidableBasesUI.Patches;

/// <summary>
/// Intercepts cui.endtest when args start with RB_UI so RaidableBasesUI CUI buttons work for all players.
/// Buttons use "cui.endtest RB_UI ui_buyraid ..." or "cui.endtest RB_UI rb_ui_move ...".
/// </summary>
[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Cui_Endtest_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg args)
    {
        var a = args?.Args;
        if (a == null || a.Length < 2)
            return true;
        if (a[0] != "RB_UI")
            return true;

        var player = args.Connection?.player as BasePlayer;
        if (player == null)
            return false;

        var cmd = a[1];
        if (cmd == "ui_buyraid")
        {
            if (a.Length >= 3 && a[2] == "closeui")
            {
                RaidableBasesUIHandler.CloseBuyableUi(player);
                return false;
            }
            if (a.Length >= 3)
            {
                RaidableBasesUIMod.RunUiBuyraidCommand(a[2]);
                return false;
            }
            return false;
        }

        if (cmd == "rb_ui_move" && a.Length >= 3)
        {
            var typeOrDir = a[2];
            if (a.Length >= 4)
            {
                RaidableBasesUIHandler.MoveBuyableUi(player, a[3]);
                return false;
            }
            if (typeOrDir == "Buyable")
            {
                RaidableBasesUIHandler.MoveBuyableUi(player, "right");
                return false;
            }
            return false;
        }

        return true;
    }
}
