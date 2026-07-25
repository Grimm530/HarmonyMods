using System;
using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

/// <summary>
/// Clients only forward ConsoleGen commands (e.g. cui.endtest). TCUpgrade CUI buttons are emitted
/// as "cui.endtest TCUPGRADE …" by <see cref="CUIHelper.NormalizeButtonCommand"/>; this marker
/// routes only the TCUPGRADE payload to the mod. Other markers (AdminMenu/TeleportGUI/etc.) pass through.
/// </summary>
[HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
public static class Cui_Endtest_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(ConsoleSystem.Arg args)
	{
		var a = args?.Args;
		if (a == null || a.Length < 1)
		{
			return true;
		}

		string marker = a[0].ToString();
		if (!string.Equals(marker, TCUpgradeMod.CuiMarker, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		TCUpgradeMod mod = TCUpgradeMod.Instance;
		if (mod == null)
		{
			return true;
		}

		try
		{
			mod.HandleSendCmdFromCui(args);
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("[TCUpgrade] cui.endtest TCUPGRADE: " + ex));
		}

		return false;
	}
}
