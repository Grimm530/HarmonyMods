using System;
using HarmonyLib;

namespace InventoryShortcuts.Patches;

/// <summary>
/// Intercepts /gridlines chat command. Admin only: shows the percentage grid overlay on screen.
/// </summary>
[HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
public static class Chat_Say_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;
        string msg = arg.GetString(0, "")?.Trim();
        if (string.IsNullOrEmpty(msg)) return true;

        if (!msg.Equals("/gridlines", StringComparison.OrdinalIgnoreCase)) return true;

        var mod = InventoryShortcutsMod.Instance;
        if (mod == null) return true;

        var player = arg.Connection?.player as BasePlayer;
        if (player == null || player.IsDestroyed || !player.IsConnected) return true;

        if (!player.IsAdmin)
        {
            player.ChatMessage("Only admins can use /gridlines.");
            return false;
        }

        mod.ShowGridOverlay(player);
        return false;
    }
}
