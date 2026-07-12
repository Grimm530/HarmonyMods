using ConVar;
using HarmonyLib;
using UnityEngine;

namespace RecyclerSpeed.Patches;

[HarmonyPatch(typeof(Chat), nameof(Chat.say))]
internal static class Chat_Say_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(ConsoleSystem.Arg arg)
	{
		if (arg == null) return true;
		var msg = arg.GetString(0, "text")?.Trim();
		if (string.IsNullOrEmpty(msg)) return true;
		var mod = RecyclerSpeedMod.Instance;
		if (mod == null) return true;
		bool handled = mod.OnChatSay(arg.Player(), msg);
		return !handled;
	}
}
