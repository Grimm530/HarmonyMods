using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

/// <summary>
/// Blocks turret damage to players (attached weapons set initiator/parent to the turret, not only <see cref="AutoTurret.ApplyDamage"/>).
/// </summary>
[HarmonyPatch(typeof(BasePlayer), "Hurt", new Type[] { typeof(HitInfo) })]
[HarmonyPriority(Priority.First)]
public static class BasePlayer_Hurt_AutoTurretIgnorePlayers_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(BasePlayer __instance, HitInfo info)
	{
		if (TurretTargetingHelpers.TryBlockTurretPlayerDamage(__instance, info, "Hurt"))
		{
			return false;
		}
		return true;
	}
}
