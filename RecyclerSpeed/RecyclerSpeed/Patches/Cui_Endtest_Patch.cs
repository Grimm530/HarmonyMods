using HarmonyLib;
using UnityEngine;

namespace RecyclerSpeed.Patches;

[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
internal static class Cui_Endtest_Patch
{
	private static string[] ToStringArray(Facepunch.StringView[] args)
	{
		if (args == null || args.Length == 0) return System.Array.Empty<string>();

		var result = new string[args.Length];
		for (int i = 0; i < args.Length; i++)
			result[i] = args[i].ToString();
		return result;
	}

	[HarmonyPrefix]
	public static bool Prefix(ConsoleSystem.Arg args)
	{
		var a = args?.Args;
		if (a == null || a.Length < 2 || !string.Equals(a[0].ToString(), "RECYCLER_SPEED", System.StringComparison.OrdinalIgnoreCase))
			return true;
		var mod = RecyclerSpeedMod.Instance;
		if (mod == null) return true;
		var player = args.Player();
		if (player == null) return true;
		bool handled = mod.HandleCuiCommand(player, ToStringArray(a));
		return !handled;
	}
}
