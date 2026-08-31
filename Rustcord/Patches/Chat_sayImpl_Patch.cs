using System;
using ConVar;
using HarmonyLib;
using Network;
using UnityEngine;

namespace Rustcord.Patches;

/// <summary>Postfix on Chat.sayImpl - after chat is processed, send to Discord.</summary>
[HarmonyPatch(typeof(Chat), "sayImpl", new Type[] { typeof(Chat.ChatChannel), typeof(ConsoleSystem.Arg) })]
internal class Chat_sayImpl_Patch
{
    [HarmonyPostfix]
    static void Postfix(Chat.ChatChannel targetChannel, ConsoleSystem.Arg arg)
    {
        if (RustcordMod.Instance == null) return;
        var cfg = RustcordConfig.Config;
        if (cfg?.PostSettings?.PlayerChat != true) return;

        var player = arg?.Player();
        if (player == null || !player.IsValid()) return;

        var rawMessage = arg.GetString(0, "text");
        if (string.IsNullOrEmpty(rawMessage)) return;

        // Skip Oxide/plugin chat commands – don't send to Discord (e.g. /kit, /pm, /clan)
        var msgTrim = rawMessage.TrimStart();
        if (msgTrim.Length > 0 && (msgTrim[0] == '/' || msgTrim[0] == '\\')) return;

        var message = RustcordMod.ApplyFilter(rawMessage);
        var serverName = cfg?.General?.ServerName ?? "";
        var formatted = targetChannel == Chat.ChatChannel.Team
            ? RustcordMod.FormatChat(serverName, player.displayName ?? "?", message)
            : RustcordMod.FormatChat(serverName, player.displayName ?? "?", message);

        var perm = targetChannel == Chat.ChatChannel.Team ? "msg_teamchat" : "msg_chat";
        RustcordMod.PostToDiscord(formatted, perm);
    }
}
