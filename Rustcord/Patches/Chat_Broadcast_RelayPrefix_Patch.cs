using ConVar;
using HarmonyLib;

namespace Rustcord.Patches;

/// <summary>
/// When the ticket-support-system relay sends messages via RCON say, the game displays "SERVER" as the prefix.
/// This patch hides "SERVER" for relay messages (SVR1, SVR2, SVR3, [Discord] etc.) so they appear as just the tag + content.
/// Relay messages from rustcordRelay use format: &lt;color=#55aaff&gt;TAG&lt;/color&gt; Name: message
/// </summary>
[HarmonyPatch(typeof(Chat), nameof(Chat.Broadcast))]
internal class Chat_Broadcast_RelayPrefix_Patch
{
	[HarmonyPrefix]
	private static void Prefix(string message, ref string username)
	{
		if (username != "SERVER") return;
		// Relay messages from ticket-support-system: colored tag (SVR1, SVR2, [Discord], etc.) or legacy [Discord] prefix
		if (message != null && (message.StartsWith("<color=") || message.StartsWith("[Discord]")))
			username = "";
	}
}
