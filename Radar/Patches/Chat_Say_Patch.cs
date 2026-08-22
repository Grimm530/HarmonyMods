using System;
using ConVar;
using HarmonyChat;
using HarmonyLib;
using UnityEngine;

namespace Radar.Patches;

/// <summary>
/// Route /radar through ChatSayBridge so DynamicCupShare / RemoverTool / other
/// Priority.First prefixes cannot swallow it. HarmonyX skips remaining prefixes
/// when one returns false, and vanilla sayAs silently drops slash commands.
/// Patch both say and localsay — clients use either depending on chat mode.
/// </summary>
internal static class ChatCommandRouter
{
    internal static bool TryHandle(ConsoleSystem.Arg arg)
    {
        if (arg == null) return false;

        string text = arg.GetString(0, "text")?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            try
            {
                if (arg.Args != null && arg.Args.Length > 0)
                    text = arg.Args[0].ToString()?.Trim();
            }
            catch { }
        }
        if (string.IsNullOrEmpty(text)) return false;
        if (!text.StartsWith("/") && !text.StartsWith("\\")) return false;

        BasePlayer player = arg.Player() ?? arg.Connection?.player as BasePlayer;
        if (player == null || !player.IsConnected) return false;

        try
        {
            if (ChatSayBridge.Dispatch(player, text))
                return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Radar] ChatSayBridge: " + ex.Message);
        }

        var mod = RadarMod.Instance;
        if (mod == null) return false;
        return mod.OnChatSay(player, text);
    }
}

[HarmonyPatch(typeof(Chat), nameof(Chat.say))]
internal static class Chat_Say_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ConsoleSystem.Arg arg)
    {
        try
        {
            if (ChatCommandRouter.TryHandle(arg))
                return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Radar] Chat.say: " + ex.Message);
        }
        return true;
    }
}

[HarmonyPatch(typeof(Chat), nameof(Chat.localsay))]
internal static class Chat_LocalSay_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ConsoleSystem.Arg arg)
    {
        try
        {
            if (ChatCommandRouter.TryHandle(arg))
                return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Radar] Chat.localsay: " + ex.Message);
        }
        return true;
    }
}
