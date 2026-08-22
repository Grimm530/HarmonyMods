using System;
using HarmonyChat;
using HarmonyLib;
using UnityEngine;

namespace DynamicCupShareHarmony.Patches
{
    /// <summary>
    /// Claim /share before other Chat.say prefixes. HarmonyX stops remaining prefixes
    /// when one returns false, so this runs first and Dispatches ChatSayBridge so
    /// /radar, /codelock, /remove, and other registered commands still work.
    /// </summary>
    internal static class ChatCommandRouter
    {
        internal static bool TryHandle(ConsoleSystem.Arg arg)
        {
            if (arg == null) return false;

            string text = arg.GetString(0, string.Empty)?.Trim();
            if (string.IsNullOrEmpty(text))
                text = arg.GetString(0, "text")?.Trim();
            if (string.IsNullOrEmpty(text) || (!text.StartsWith("/") && !text.StartsWith("\\")))
                return false;

            BasePlayer player = arg.Player() ?? arg.Connection?.player as BasePlayer;
            if (player == null || !player.IsConnected) return false;

            try
            {
                if (ChatSayBridge.Dispatch(player, text))
                    return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DynamicCupShare] ChatSayBridge: " + ex.Message);
            }

            var mod = DynamicCupShareMod.Instance;
            if (mod == null) return false;
            return mod.OnChatCommand(player, text);
        }
    }

    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.say))]
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
                Debug.LogWarning("[DynamicCupShare] Chat.say: " + ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ConVar.Chat), nameof(ConVar.Chat.localsay))]
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
                Debug.LogWarning("[DynamicCupShare] Chat.localsay: " + ex.Message);
            }
            return true;
        }
    }
}
