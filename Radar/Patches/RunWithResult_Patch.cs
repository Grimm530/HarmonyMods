using System;
using HarmonyLib;
using UnityEngine;

namespace Radar.Patches;

/// <summary>
/// When the client sends "radar" (e.g. from chat box /radar), handle it here so /radar works even if
/// the client didn't receive "radar" in its replicated command list (e.g. joined before mod load).
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

        if (!player.IsAdmin && !player.IsDeveloper)
        {
            Radar.RadarMod.SendMessage(player, "Radar requires admin.");
            __result = new ConsoleSystem.CommandResult(ConsoleSystem.CommandResultType.Success, null, null);
            return false;
        }

        mod.ToggleRadar(player);
        __result = new ConsoleSystem.CommandResult(ConsoleSystem.CommandResultType.Success, null, null);
        return false;
    }
}
