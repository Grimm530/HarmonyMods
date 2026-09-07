using System;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

/// <summary>
/// Mirrors vanilla <see cref="BasePlayer.Hurt(HitInfo)"/> PvP reflection when <c>server.pve</c> is on, and also when
/// config requests game PvE even if the convar was cleared elsewhere. Skips reflection for NPCs / pets (see <see cref="ShouldNotReflect"/>).
/// </summary>
[HarmonyPatch(typeof(BasePlayer), "Hurt", new Type[] { typeof(HitInfo) })]
[HarmonyPriority(Priority.First)]
public static class BasePlayer_Hurt_NoReflectAnimalsPets_Patch
{
	private static BasePlayer _skipReflectTo;

	[HarmonyPrefix]
	public static bool Prefix(BasePlayer __instance, HitInfo info)
	{
		if (info == null)
		{
			return true;
		}
		TruePVEMod instance = TruePVEMod.Instance;
		BasePlayer playerInitiator = PvEDamageHelpers.ResolvePlayerInitiator(info);
		if (instance != null && instance.Config?.PvE?.ProtectSleepingPlayers == true && __instance.IsSleeping() && (Object)(object)playerInitiator != (Object)null && (Object)(object)playerInitiator != (Object)(object)__instance)
		{
			info.damageTypes?.Clear();
			return false;
		}
		if ((Object)(object)_skipReflectTo == (Object)(object)__instance)
		{
			_skipReflectTo = null;
			return false;
		}
		// Same rule as vanilla BasePlayer.Hurt: reflect player→player damage to the attacker (attacker takes damage, victim none).
		// Runs when server.pve OR our config still asks for game PvE (covers convar cleared after load / load order).
		bool wantReflect = ConVar.Server.pve || (instance?.Config?.PvE?.EnableGamePvE ?? false);
		if (wantReflect && (Object)(object)playerInitiator != (Object)null && (Object)(object)playerInitiator != (Object)(object)__instance && !__instance.IsNpc && !ShouldNotReflect(__instance))
		{
			float total = info.damageTypes?.Total() ?? 0f;
			if (total > 0f)
			{
				playerInitiator.Hurt(total, Rust.DamageType.Generic);
			}
			info.damageTypes?.Clear();
			return false;
		}
		if (ConVar.Server.pve && (Object)(object)playerInitiator != (Object)null && (Object)(object)playerInitiator != (Object)(object)__instance && ShouldNotReflect(__instance))
		{
			_skipReflectTo = playerInitiator;
		}
		return true;
	}

	private static bool ShouldNotReflect(BasePlayer victim)
	{
		if ((Object)(object)victim == (Object)null)
		{
			return false;
		}
		if (victim.IsNpc)
		{
			return true;
		}
		switch (((object)victim).GetType().Name)
		{
		case "FrankensteinPet":
			return true;
		case "FrankensteinPet2":
		case "FrankensteinPet3":
			return true;
		default:
			if (!victim.IsConnected)
			{
				return true;
			}
			return false;
		}
	}
}
