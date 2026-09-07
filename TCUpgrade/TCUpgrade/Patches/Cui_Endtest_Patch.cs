using HarmonyLib;

namespace TCUpgrade.Patches;

/// <summary>
/// Intercepts cui.endtest when args start with SENDCMD so TCUpgrade CUI buttons work for all players
/// (including after mod reload). Buttons use "cui.endtest SENDCMD ..." (vanilla replicated command).
/// </summary>
[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Cui_Endtest_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(ConsoleSystem.Arg args)
	{
		var a = args?.Args;
		if (a == null || a.Length < 1)
			return true;
		if (a.Length == 1 && !a[0].StartsWith("SENDCMD"))
			return true;
		if (a.Length >= 2 && a[0] != "SENDCMD")
			return true;

		var mod = TCUpgradeMod.Instance;
		if (mod == null) return true;

		mod.HandleSendCmdFromCui(args);
		return false;
	}
}
