using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace Rustcord.Patches;

/// <summary>
/// Game chat → Discord. BetterChat prefixes Chat.say / sayImpl and returns false, which skips the
/// original method. Harmony still runs postfixes, so we hook both: Chat.say (global, the common path)
/// and sayImpl (team/local and anything that still reaches it).
/// </summary>
internal static class ChatPost
{
	private static string _lastKey;
	private static float _lastTime;

	internal static void TryPost(Chat.ChatChannel targetChannel, ConsoleSystem.Arg arg)
	{
		if (RustcordMod.Instance == null) return;
		var cfg = RustcordConfig.Config;
		if (cfg?.PostSettings?.PlayerChat != true) return;

		var player = arg?.Player() ?? arg?.Connection?.player as BasePlayer;
		if (player == null || !player.IsValid()) return;

		var rawMessage = arg.GetString(0, "text");
		if (string.IsNullOrEmpty(rawMessage)) return;

		var msgTrim = rawMessage.TrimStart();
		if (msgTrim.Length > 0 && (msgTrim[0] == '/' || msgTrim[0] == '\\')) return;

		var key = player.userID + ":" + targetChannel + ":" + rawMessage;
		var now = UnityEngine.Time.realtimeSinceStartup;
		if (key == _lastKey && now - _lastTime < 0.75f) return;
		_lastKey = key;
		_lastTime = now;

		var message = RustcordMod.ApplyFilter(rawMessage);
		var serverName = cfg?.General?.ServerName ?? "";
		var formatted = RustcordMod.FormatChat(serverName, player.displayName ?? "?", message);
		var perm = targetChannel == Chat.ChatChannel.Team ? "msg_teamchat" : "msg_chat";
		RustcordMod.PostToDiscord(formatted, perm);
	}
}

[HarmonyPatch(typeof(Chat), "sayImpl", new Type[] { typeof(Chat.ChatChannel), typeof(ConsoleSystem.Arg) })]
internal class Chat_sayImpl_Patch
{
	[HarmonyPostfix]
	static void Postfix(Chat.ChatChannel targetChannel, ConsoleSystem.Arg arg)
	{
		ChatPost.TryPost(targetChannel, arg);
	}
}

[HarmonyPatch(typeof(Chat), nameof(Chat.say))]
internal class Chat_say_Patch
{
	[HarmonyPostfix]
	static void Postfix(ConsoleSystem.Arg arg)
	{
		var channel = Chat.globalchat ? Chat.ChatChannel.Global : Chat.ChatChannel.Local;
		ChatPost.TryPost(channel, arg);
	}
}
