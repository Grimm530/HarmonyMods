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
	[HarmonyPrefix]
	public static bool Prefix(BasePlayer __instance, HitInfo info, ref bool __state)
	{
		__state = false;
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
		// Same rule as vanilla BasePlayer.Hurt: reflect player→player damage to the attacker (attacker takes damage, victim none).
		// Runs when server.pve OR our config still asks for game PvE (covers convar cleared after load / load order).
		bool wantReflect = ConVar.Server.pve || (instance?.Config?.PvE?.EnableGamePvE ?? false);
		bool bypassReflection = ShouldBypassPveReflection(__instance, info);
		if (wantReflect && bypassReflection)
		{
			if (ConVar.Server.pve)
			{
				ConVar.Server.pve = false;
				__state = true;
			}
			return true;
		}
		if (wantReflect && (Object)(object)playerInitiator != (Object)null && (Object)(object)playerInitiator != (Object)(object)__instance && !__instance.IsNpc)
		{
			float total = info.damageTypes?.Total() ?? 0f;
			if (total > 0f)
			{
				playerInitiator.Hurt(total, Rust.DamageType.Generic);
			}
			info.damageTypes?.Clear();
			return false;
		}
		return true;
	}

	[HarmonyPostfix]
	public static void Postfix(bool __state)
	{
		if (__state)
		{
			ConVar.Server.pve = true;
		}
	}

	private static bool ShouldBypassPveReflection(BasePlayer victim, HitInfo info)
	{
		if ((Object)(object)victim == (Object)null)
		{
			return false;
		}
		BasePlayer directInitiator = info?.Initiator as BasePlayer;
		if ((Object)(object)directInitiator == (Object)null || (Object)(object)directInitiator == (Object)(object)victim)
		{
			return false;
		}
		if (directInitiator.IsNpc || !LootDefenderHelpers.IsSteamId(directInitiator.userID))
		{
			return true;
		}
		if (victim.IsNpc)
		{
			return true;
		}
		if (!LootDefenderHelpers.IsSteamId(victim.userID))
		{
			return true;
		}
		switch (((object)victim).GetType().Name)
		{
		case "FrankensteinPet":
		case "FrankensteinPet2":
		case "FrankensteinPet3":
		case "CustomScientistNpc":
		case "CustomScientistNPC":
		case "ZombieNPC":
		case "HumanoidNPC":
			return true;
		default:
			return false;
		}
	}
}
