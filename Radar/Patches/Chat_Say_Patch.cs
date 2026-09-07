using HarmonyLib;
using UnityEngine;

namespace Radar.Patches;

[HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
public static class Chat_Say_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;
        var msg = arg.GetString(0, "text")?.Trim();
        if (string.IsNullOrEmpty(msg)) return true;
        var mod = Radar.RadarMod.Instance;
        if (mod == null) return true;
        bool handled = mod.OnChatSay(arg.Player(), msg);
        return !handled;
    }
}
