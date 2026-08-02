using HarmonyLib;
using UnityEngine;

namespace CopyPasteHarmony.Patches;

[HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
public static class Chat_Say_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;
        var mod = CopyPasteHarmonyMod.Instance;
        if (mod == null) return true;

        string message = arg.GetString(0, "text")?.Trim();
        if (string.IsNullOrEmpty(message)) return true;

        bool handled = mod.TryHandleChatCommand(arg.Player(), message);
        return !handled;
    }
}

