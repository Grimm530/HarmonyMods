using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace ChatFilter.Patches
{
    /// <summary>
    /// Runs first. If message contains a bad word: replace it with the filtered text and let the rest of the pipeline
    /// (ChatTranslator, game sayImpl, Rustcord) run — they all read arg.GetString(0), which we override to the filtered message.
    /// Block entire message only when it becomes empty after filtering or when Whole Message Filter is on.
    /// </summary>
    [HarmonyPatch(typeof(Chat), "sayImpl", new Type[] { typeof(Chat.ChatChannel), typeof(ConsoleSystem.Arg) })]
    [HarmonyPriority(HarmonyLib.Priority.First)]
    public static class Chat_sayImpl_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Chat.ChatChannel targetChannel, ConsoleSystem.Arg arg)
        {
            // Clear previous message's override so we don't leak it to the next call
            ChatFilterMod.ClearFilterOverride();

            if (arg == null) return true;
            var player = arg.Player();
            if (player == null || !player.IsValid()) return true;

            var cfg = ChatFilterConfig.Config;
            if (cfg == null) return true;

            if (cfg.ExcludeTeamChat && targetChannel == Chat.ChatChannel.Team)
                return true;

            if (cfg.BlockSpecialCharacters)
            {
                var raw = arg.GetString(0, "text");
                if (!string.IsNullOrEmpty(raw) && ChatFilterMod.HasSpecialChars(raw))
                {
                    ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, 0, "Special characters in chat are not allowed.");
                    return false;
                }
            }

            if (!cfg.WordFilterEnabled) return true;

            var message = arg.GetString(0, "text");
            if (string.IsNullOrEmpty(message)) return true;

            var mod = ChatFilterMod.Instance;
            if (mod == null) return true;

            var filtered = mod.FilterMessage(player, message, out bool hadMatch, out bool filterAll);

            // Block entire message only if filter-all is on or message is empty after filtering
            if (filterAll || (hadMatch && string.IsNullOrWhiteSpace(filtered)))
            {
                ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, 0, "Your message was blocked.");
                return false;
            }

            if (hadMatch)
            {
                if (!ChatFilterMod.ShouldExclude(player))
                    mod.RecordOffense(player);
                // Replace message for this call: anyone who reads arg.GetString(0) gets the filtered text
                ChatFilterMod.SetFilterOverride(filtered);
            }

            return true; // Let ChatTranslator, sayImpl, Rustcord run; they'll see the filtered message via GetString
        }
    }
}
