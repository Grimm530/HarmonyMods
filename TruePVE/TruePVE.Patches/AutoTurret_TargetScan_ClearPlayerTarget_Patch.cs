using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

/// <summary>
/// Drops an active player target when ignore-players rules apply (e.g. after config load or mode change).
/// </summary>
[HarmonyPatch(typeof(AutoTurret), "TargetScan")]
public static class AutoTurret_TargetScan_ClearPlayerTarget_Patch
{
	[HarmonyPrefix]
	public static void Prefix(AutoTurret __instance)
	{
		if (__instance.target is BasePlayer player && TurretTargetingHelpers.ShouldBlockPlayerTarget(__instance, player, "TargetScan"))
		{
			__instance.SetTarget(null);
		}
	}
}
