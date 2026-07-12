using System;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(BasePlayer), "OnAttacked", new Type[] { typeof(HitInfo) })]
[HarmonyPriority(Priority.First)]
public static class BasePlayer_OnAttacked_AutoTurretIgnorePlayers_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(BasePlayer __instance, HitInfo info)
	{
		if (TurretTargetingHelpers.TryBlockTurretPlayerDamage(__instance, info, "OnAttacked"))
		{
			return false;
		}
		return true;
	}
}
