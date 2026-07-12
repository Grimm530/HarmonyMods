using HarmonyLib;
using UnityEngine;

namespace Radar.Patches;

[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Cui_Endtest_Patch
{
    private static string[] ToStringArray(Facepunch.StringView[] args)
    {
        if (args == null || args.Length == 0) return System.Array.Empty<string>();

        var result = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
            result[i] = args[i].ToString();
        return result;
    }

    /// <summary>Resolve the player who sent the command (connection owner). When spectating, arg.Player() can be null or wrong; use connection.userid so radar works.</summary>
    private static BasePlayer GetPlayerFromArg(ConsoleSystem.Arg args)
    {
        if (args?.Connection == null) return null;
        var p = args.Connection.player as BasePlayer;
        if (p != null) return p;
        return BasePlayer.FindByID(args.Connection.userid);
    }

    /// <summary>Handle only RADAR; return true when not RADAR so TCUpgrade (cui.endtest SENDCMD ...) and other mods work.</summary>
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg args)
    {
        var a = args?.Args;
        if (a == null || a.Length < 2 || !string.Equals(a[0].ToString(), "RADAR", System.StringComparison.OrdinalIgnoreCase)) return true;
        var mod = Radar.RadarMod.Instance;
        if (mod == null) return true;
        var player = GetPlayerFromArg(args);
        if (player == null) return true;
        bool handled = mod.HandleCuiCommand(player, ToStringArray(a));
        return !handled;
    }
}
