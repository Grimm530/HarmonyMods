using System;
using System.Collections.Generic;
using ConVar;
using Facepunch;
using HarmonyLib;
using Network;
using UnityEngine;

namespace ChatTranslator.Patches;

[HarmonyPatch(typeof(Chat), "sayImpl", new Type[] { typeof(Chat.ChatChannel), typeof(ConsoleSystem.Arg) })]
internal class Chat_sayImpl_Patch
{
    private static bool Prefix(Chat.ChatChannel targetChannel, ConsoleSystem.Arg arg)
    {
        if (ChatTranslatorMod.Instance == null || !ChatTranslatorMod.IsTranslationAPIAvailable())
            return true; // Let original run

        var config = ChatTranslatorConfig.Config;
        if (config == null) return true;

        if (!Chat.enabled)
        {
            arg.ReplyWith("Chat is disabled.");
            return false;
        }

        var player = arg.Player();
        if (!player || (Chat.hideChatInTutorial && player.IsInTutorial) || player.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute))
        {
            return false;
        }

        // Handle /lang command
        var rawMessage = arg.GetString(0, "text");
        if (TryHandleLangCommand(player, rawMessage, arg))
        {
            return false;
        }

        // Rate limiting (replicate from sayImpl)
        if (!player.IsAdmin && !player.IsDeveloper)
        {
            if (player.NextChatTime == 0f)
                player.NextChatTime = UnityEngine.Time.realtimeSinceStartup - 30f;
            if (player.NextChatTime > UnityEngine.Time.realtimeSinceStartup)
            {
                player.NextChatTime += 2f;
                var remaining = player.NextChatTime - UnityEngine.Time.realtimeSinceStartup;
                ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 2, 0,
                    "You're chatting too fast - try again in " + (remaining + 0.5f).ToString("0") + " seconds");
                if (remaining > 120f)
                    player.Kick("Chatting too fast");
                return false;
            }
        }

        var message = rawMessage.Replace("\n", "").Replace("\r", "").Trim();
        if (message.Length > 128) message = message.Substring(0, 128);
        if (message.Length <= 0) return false;
        // Oxide plugin chat commands (/skilltree, /sortbutton, etc.) - let original sayImpl run so Oxide receives them
        if (message.StartsWith("/") || message.StartsWith("\\")) return true;

        message = message.EscapeRichText();
        var username = player.displayName.EscapeRichText();
        var userId = player.userID;
        var nameColor = GetNameColor(userId, player);

        player.NextChatTime = UnityEngine.Time.realtimeSinceStartup + 1.5f;

        // Determine recipients and translate for each
        switch (targetChannel)
        {
            case Chat.ChatChannel.Global:
                TranslateAndSendToAll(player, message, username, userId, nameColor, 0, targetChannel);
                break;
            case Chat.ChatChannel.Team:
                var team = RelationshipManager.ServerInstance.FindPlayersTeam(userId);
                if (team == null) return false;
                var connections = team.GetOnlineMemberConnections();
                if (connections != null)
                    TranslateAndSendToConnections(connections, player, message, username, userId, nameColor, 1, targetChannel);
                break;
            case Chat.ChatChannel.Local:
                TranslateAndSendLocal(player, message, username, userId, nameColor, targetChannel);
                break;
            case Chat.ChatChannel.Cards:
                return true; // Let original handle card game chat
            default:
                return true; // Clan, etc. - let original handle
        }

        return false; // Skip original
    }

    private static bool TryHandleLangCommand(BasePlayer player, string message, ConsoleSystem.Arg arg)
    {
        var trimmed = message?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith("/lang", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            var current = ChatTranslatorMod.GetLanguage(player.UserIDString) ?? "en";
            player.SendConsoleCommand("chat.add", 2, 0,
                $"<color=#55aaff>[ChatTranslator]</color> Your language: {current}. Use /lang &lt;code&gt; (e.g. /lang es)");
            return true;
        }

        var code = parts[1].Trim();
        ChatTranslatorMod.SetLanguage(player.UserIDString, code);
        player.SendConsoleCommand("chat.add", 2, 0,
            $"<color=#55aaff>[ChatTranslator]</color> Language set to {code}");
        return true;
    }

    private static void TranslateAndSendToAll(BasePlayer sender, string originalMessage, string username, ulong userId,
        string nameColor, int channel, Chat.ChatChannel targetChannel)
    {
        var config = ChatTranslatorConfig.Config;
        var senderId = sender.UserIDString;

        SendRConForRelay(sender, originalMessage, senderId, targetChannel);

        foreach (var target in BasePlayer.activePlayerList)
        {
            if (target == null || !target.IsConnected) continue;
            if (sender == target && !config.TranslateForSender)
            {
                SendMessageToPlayer(target, channel, userId, originalMessage, username, nameColor);
                continue;
            }

            ChatTranslatorMod.Translate(originalMessage, target.UserIDString, senderId, translated =>
            {
                if (config.ShowBothMessages && translated != originalMessage)
                {
                    ChatTranslatorMod.Translate("Translation", senderId, senderId, prefix =>
                    {
                        var combined = $"{originalMessage}\n{prefix}: {translated}";
                        SendMessageToPlayer(target, channel, userId, combined, username, nameColor);
                        MaybeLogAndRCon(sender, combined, targetChannel);
                    });
                }
                else
                {
                    SendMessageToPlayer(target, channel, userId, translated, username, nameColor);
                    MaybeLogAndRCon(sender, translated, targetChannel);
                }
            });
        }
    }

    private static void TranslateAndSendToConnections(List<Network.Connection> connections, BasePlayer sender,
        string originalMessage, string username, ulong userId, string nameColor, int channel, Chat.ChatChannel targetChannel)
    {
        var config = ChatTranslatorConfig.Config;
        var senderId = sender.UserIDString;

        SendRConForRelay(sender, originalMessage, senderId, targetChannel);

        foreach (var conn in connections)
        {
            var target = conn?.player as BasePlayer;
            if (target == null || !target.IsConnected) continue;

            if (sender == target && !config.TranslateForSender)
            {
                SendMessageToConnection(conn, channel, userId, originalMessage, username, nameColor);
                continue;
            }

            ChatTranslatorMod.Translate(originalMessage, target.UserIDString, senderId, translated =>
            {
                if (config.ShowBothMessages && translated != originalMessage)
                {
                    ChatTranslatorMod.Translate("Translation", senderId, senderId, prefix =>
                    {
                        var combined = $"{originalMessage}\n{prefix}: {translated}";
                        SendMessageToConnection(conn, channel, userId, combined, username, nameColor);
                        MaybeLogAndRCon(sender, combined, targetChannel);
                    });
                }
                else
                {
                    SendMessageToConnection(conn, channel, userId, translated, username, nameColor);
                    MaybeLogAndRCon(sender, translated, targetChannel);
                }
            });
        }
    }

    private static void TranslateAndSendLocal(BasePlayer sender, string originalMessage, string username, ulong userId,
        string nameColor, Chat.ChatChannel targetChannel)
    {
        var rangeSq = Chat.localChatRange * Chat.localChatRange;
        var config = ChatTranslatorConfig.Config;
        var senderId = sender.UserIDString;

        SendRConForRelay(sender, originalMessage, senderId, targetChannel);

        foreach (var target in BasePlayer.activePlayerList)
        {
            if (target == null || !target.IsConnected) continue;
            var sqrDist = (target.transform.position - sender.transform.position).sqrMagnitude;
            if (sqrDist > rangeSq) continue;

            if (sender == target && !config.TranslateForSender)
            {
                SendMessageToPlayer(target, 4, userId, originalMessage, username, nameColor,
                    Mathf.Clamp01(sqrDist / rangeSq + 0.2f));
                continue;
            }

            ChatTranslatorMod.Translate(originalMessage, target.UserIDString, senderId, translated =>
            {
                var vol = Mathf.Clamp01(sqrDist / rangeSq + 0.2f);
                if (config.ShowBothMessages && translated != originalMessage)
                {
                    ChatTranslatorMod.Translate("Translation", senderId, senderId, prefix =>
                    {
                        var combined = $"{originalMessage}\n{prefix}: {translated}";
                        SendMessageToPlayer(target, 4, userId, combined, username, nameColor, vol);
                        MaybeLogAndRCon(sender, combined, targetChannel);
                    });
                }
                else
                {
                    SendMessageToPlayer(target, 4, userId, translated, username, nameColor, vol);
                    MaybeLogAndRCon(sender, translated, targetChannel);
                }
            });
        }
    }

    private static void SendMessageToPlayer(BasePlayer target, int channel, ulong userId, string message,
        string username, string nameColor, float volume = 1f)
    {
        if (target == null || !target.IsConnected) return;
        target.SendConsoleCommand("chat.add2", channel, userId, message, username, nameColor, volume);
    }

    private static void SendMessageToConnection(Network.Connection conn, int channel, ulong userId, string message,
        string username, string nameColor, float volume = 1f)
    {
        if (conn == null) return;
        ConsoleNetwork.SendClientCommand(conn, "chat.add2", channel, userId, message, username, nameColor, volume);
    }

    /// <summary>Always sends one RCON broadcast per chat message (no longer tied to Log translated chat messages).</summary>
    private static void SendRConForMessage(BasePlayer sender, string message, Chat.ChatChannel channel)
    {
        if (sender == null) return;
        var unixTime = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        RCon.Broadcast(RCon.LogType.Chat, new Chat.ChatEntry
        {
            Channel = channel,
            Message = message,
            UserId = sender.UserIDString,
            Username = sender.displayName,
            Color = GetNameColor(sender.userID, sender),
            Time = unixTime
        });
    }

    /// <summary>
    /// Sends one RCON relay line translated to default/server language so Discord relay receives normalized text.
    /// Falls back to the original message on any translation failure/unavailability.
    /// </summary>
    private static void SendRConForRelay(BasePlayer sender, string originalMessage, string senderId, Chat.ChatChannel channel)
    {
        if (sender == null) return;

        // No API => preserve previous behavior
        if (!ChatTranslatorMod.IsTranslationAPIAvailable())
        {
            SendRConForMessage(sender, originalMessage, channel);
            return;
        }

        // Empty targetId means ChatTranslator falls back to default "en" (or server default if ForceServerDefault=true).
        ChatTranslatorMod.Translate(originalMessage, string.Empty, senderId, translated =>
        {
            var relayMessage = string.IsNullOrEmpty(translated) ? originalMessage : translated;
            SendRConForMessage(sender, relayMessage, channel);
        });
    }

    /// <summary>Log to server console only when "Log translated chat messages" is enabled. RCON is handled by SendRConForMessage.</summary>
    private static void MaybeLogAndRCon(BasePlayer sender, string message, Chat.ChatChannel channel)
    {
        if (ChatTranslatorConfig.Config?.LogChatMessages != true) return;
        if (Chat.serverlog)
            ServerConsole.PrintColoured($"[{channel}] {sender?.displayName}: {message}", ConsoleColor.Green);
    }

    private static string GetNameColor(ulong userId, BasePlayer player = null)
    {
        var userGroup = ServerUsers.Get(userId)?.group ?? ServerUsers.UserGroup.None;
        var isMod = userGroup == ServerUsers.UserGroup.Owner || userGroup == ServerUsers.UserGroup.Moderator;
        var isDev = player != null ? player.IsDeveloper : DeveloperList.Contains(userId);
        if (isDev) return "#fa5";
        if (isMod) return "#af5";
        return "#5af";
    }
}
