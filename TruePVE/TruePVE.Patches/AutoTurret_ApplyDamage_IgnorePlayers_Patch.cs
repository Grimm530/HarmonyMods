using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

/// <summary>
/// Prevents turret bullets from damaging real players (including stray hits while locked on NPCs).
/// </summary>
[HarmonyPatch(typeof(AutoTurret), "ApplyDamage", new Type[]
{
	typeof(BaseCombatEntity),
	typeof(Vector3),
	typeof(Vector3)
})]
public static class AutoTurret_ApplyDamage_IgnorePlayers_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(AutoTurret __instance, BaseCombatEntity entity)
	{
		if (entity is BasePlayer player && TurretTargetingHelpers.ShouldBlockPlayerDamage(__instance, player, "ApplyDamage"))
		{
			return false;
		}
		return true;
	}
}
