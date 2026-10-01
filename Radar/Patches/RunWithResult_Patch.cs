using System;
using HarmonyLib;
using UnityEngine;

namespace Radar.Patches;

/// <summary>
/// When the client sends "radar" (e.g. from chat box /radar), handle it here so /radar works even if
/// the client didn't receive "radar" in its replicated command list (e.g. joined before mod load).
/// Also routes <c>radar findbyitem &lt;shortname&gt;</c> (AdminRadar 5.4.312).
/// </summary>
[HarmonyPatch(typeof(ConsoleSystem), nameof(ConsoleSystem.RunWithResult), new Type[] { typeof(ConsoleSystem.Option), typeof(string), typeof(object[]) })]
public static class RunWithResult_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ref ConsoleSystem.CommandResult __result, ConsoleSystem.Option options, string strCommand, object[] args)
    {
        if (string.IsNullOrWhiteSpace(strCommand)) return true;
        var cmd = strCommand.Trim();
        // Match "radar" or "global.radar" (client may send either)
        if (!cmd.Equals("radar", StringComparison.OrdinalIgnoreCase)
            && !cmd.Equals("global.radar", StringComparison.OrdinalIgnoreCase))
            return true;

        var mod = Radar.RadarMod.Instance;
        if (mod == null) return true;
        var player = options.Connection?.player as BasePlayer;
        if (player == null) return true;

        mod.HandleRadarCommand(player, ToStringArray(args));
        __result = new ConsoleSystem.CommandResult(ConsoleSystem.CommandResultType.Success, null, null);
        return false;
    }

    private static string[] ToStringArray(object[] args)
    {
        if (args == null || args.Length == 0) return Array.Empty<string>();
        var result = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
            result[i] = args[i]?.ToString() ?? "";
        return result;
    }
}
