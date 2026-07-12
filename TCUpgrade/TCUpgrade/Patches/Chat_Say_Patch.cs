using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(Chat), "say")]
public static class Chat_Say_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(ConsoleSystem.Arg arg)
	{
		if (arg == null)
		{
			return true;
		}
		string[] args = TCUpgradeMod.GetArgStrings(arg);
		string text = args.Length > 0 ? args[0]?.Trim() : null;
		if (string.IsNullOrEmpty(text) || (!text.StartsWith("/") && !text.StartsWith("\\")))
		{
			return true;
		}
		BasePlayer basePlayer = arg.Connection?.player as BasePlayer;
		if ((Object)(object)basePlayer == (Object)null)
		{
			return true;
		}
		string[] array = text.Substring(1).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			return true;
		}
		TCUpgradeMod instance = TCUpgradeMod.Instance;
		if (instance == null)
		{
			return true;
		}
		string[] array2 = ((array.Length > 1) ? new string[array.Length - 1] : Array.Empty<string>());
		for (int i = 1; i < array.Length; i++)
		{
			array2[i - 1] = array[i];
		}
		return !instance.RunChatCommand(basePlayer, array[0].ToLowerInvariant(), array2);
	}
}
