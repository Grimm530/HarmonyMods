using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// Intercepts /existing and /retrieval chat commands to toggle backpack abilities.
/// </summary>
[HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
internal static class Chat_Say_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;
        var msg = arg.GetString(0, "text")?.Trim();
        if (string.IsNullOrEmpty(msg)) return true;

        var cmd = msg.Split(' ')[0].ToLowerInvariant();
        if (cmd != "/existing" && cmd != "/retrieval") return true;

        var mod = BetterBackpackMod.Instance;
        if (mod == null) return true;

        var player = arg.Player();
        if (player == null) return true;

        bool handled = mod.OnChatCommand(player, cmd);
        return !handled;
    }
}
