using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

/// <summary>
/// Blocks <see cref="AutoTurret.SetTarget"/> from acquiring real players when
/// <see cref="PvEOptions.TurretsIgnorePlayers"/> is enabled (Oxide TruePVE <c>OnTurretTarget</c>).
/// </summary>
[HarmonyPatch(typeof(AutoTurret), "SetTarget", new Type[] { typeof(BaseCombatEntity) })]
public static class AutoTurret_SetTarget_IgnorePlayers_Patch
{
	[HarmonyPrefix]
	public static void Prefix(AutoTurret __instance, ref BaseCombatEntity targ)
	{
		if (targ is BasePlayer player && TurretTargetingHelpers.ShouldBlockPlayerTarget(__instance, player, "SetTarget"))
		{
			targ = null;
		}
	}
}
