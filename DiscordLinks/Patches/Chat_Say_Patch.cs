using HarmonyLib;
using UnityEngine;

namespace DiscordLinks.Patches;

[HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
public static class Chat_Say_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;
        var player = arg.Player();
        if (player == null) return true;

        string msg = arg.GetString(0, "text")?.Trim() ?? "";
        if (msg.StartsWith("/")) msg = msg.Substring(1).Trim();
        if (!msg.Equals("link", System.StringComparison.OrdinalIgnoreCase)) return true;

        var mod = DiscordLinksMod.Instance;
        if (mod == null) return true;

        if (mod.OnChatLink(player, out string response))
        {
            ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 0, 0, response ?? "Use /link to get a code.");
            return false;
        }
        return true;
    }
}
