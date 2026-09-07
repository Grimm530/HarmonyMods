using System;
using ConVar;
using HarmonyLib;

namespace BetterChatHarmony.Patches
{
    /// <summary>
    /// NoGiveNotices parity: suppress F1 / inventory give broadcasts from chat + history.
    /// Live path is Chat.BroadcastPlayerAction; Chat.Broadcast is legacy. Chat.Record covers history/RCon.
    /// </summary>
    internal static class GiveNoticeFilter
    {
        public static bool IsGiveText(string text)
        {
            return text != null && text.IndexOf("gave", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>Oxide OnServerMessage: block SERVER broadcasts containing "gave".</summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.Broadcast))]
    [HarmonyPriority(Priority.First)]
    internal static class Chat_Broadcast_GiveNotices_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(string message, string username)
        {
            return username != "SERVER" || !GiveNoticeFilter.IsGiveText(message);
        }
    }

    /// <summary>Oxide OnPlayerActionBroadcast (self / giveall / blueprints).</summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.BroadcastPlayerAction), new Type[] { typeof(BasePlayer), typeof(string) })]
    [HarmonyPriority(Priority.First)]
    internal static class Chat_BroadcastPlayerAction_GiveNotices_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(string action)
        {
            return !GiveNoticeFilter.IsGiveText(action);
        }
    }

    /// <summary>Oxide OnPlayerActionBroadcast [2] (giveto / giveid to other player).</summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.BroadcastPlayerAction), new Type[] { typeof(BasePlayer), typeof(string), typeof(BasePlayer), typeof(string) })]
    [HarmonyPriority(Priority.First)]
    internal static class Chat_BroadcastPlayerAction_TwoSubjects_GiveNotices_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(string middle, string suffix)
        {
            if (GiveNoticeFilter.IsGiveText(middle)) return false;
            if (GiveNoticeFilter.IsGiveText(suffix)) return false;
            return true;
        }
    }

    /// <summary>
    /// Belt-and-suspenders: keep give notices out of F1 chat history and RCon even if a broadcast path is missed.
    /// </summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.Record))]
    [HarmonyPriority(Priority.First)]
    internal static class Chat_Record_GiveNotices_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Chat.ChatEntry ce)
        {
            if (ce.Channel != Chat.ChatChannel.Server) return true;
            return !GiveNoticeFilter.IsGiveText(ce.Message);
        }
    }
}
