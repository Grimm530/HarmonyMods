using HarmonyLib;
using UnityEngine;

namespace MapVoter.Patches;

/// <summary>
/// Intercepts chat.say when player types the map vote command (e.g. mvote).
/// Skips normal chat processing so the command doesn't appear in chat.
/// </summary>
[HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
public static class Chat_Say_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;

        string message = arg.GetString(0, "text")?.Trim();
        if (string.IsNullOrEmpty(message)) return true;

        var mod = MapVoterMod.Instance;
        if (mod == null) return true;

        bool handled = mod.OnChatSay(arg.Player(), message);
        return !handled;
    }
}
