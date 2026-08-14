using System;
using HarmonyLib;
using UnityEngine;
using VQ = Oxide.Plugins.VirtualQuarries;

namespace VirtualQuarriesHarmony.Patches
{
    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
    public static class Chat_Say_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg arg)
        {
            if (arg == null) return true;
            string message = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(message) || (!message.StartsWith("/") && !message.StartsWith("\\")))
                return true;
            var player = arg.Player();
            if (player == null) return true;
            try
            {
                if (VirtualQuarriesMod.Instance != null && VirtualQuarriesMod.Instance.OnChatCommand(player, message))
                    return false;
            }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] Chat.say: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            var a = args?.Args;
            if (a == null || a.Length < 1) return true;
            string marker = a[0].ToString();
            if (!string.Equals(marker, VirtualQuarriesMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
                return true;
            var mod = VirtualQuarriesMod.Instance;
            if (mod == null) return true;
            try { mod.HandleCuiEndtest(args, a); }
            catch (Exception ex) { Debug.LogWarning("[VirtualQuarries] cui.endtest VIRTUALQUARRIES: " + ex); }
            return false;
        }
    }
}
