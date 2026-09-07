using HarmonyLib;
using UnityEngine;

namespace RecyclerSpeed.Patches;

[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
internal static class Cui_Endtest_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(ConsoleSystem.Arg args)
	{
		var a = args?.Args;
		if (a == null || a.Length < 2 || !string.Equals(a[0]?.ToString(), "RECYCLER_SPEED", System.StringComparison.OrdinalIgnoreCase))
			return true;
		var mod = RecyclerSpeedMod.Instance;
		if (mod == null) return true;
		var player = args.Player();
		if (player == null) return true;
		bool handled = mod.HandleCuiCommand(player, a);
		return !handled;
	}
}
