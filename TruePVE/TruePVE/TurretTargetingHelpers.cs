using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruePVE;

/// <summary>
/// Mirrors Oxide TruePVE <c>OnTurretTarget</c> / <c>TurretsIgnorePlayers</c> for player-deployed auto turrets.
/// </summary>
public static class TurretTargetingHelpers
{
	private static readonly Dictionary<string, DateTime> DebugThrottle = new Dictionary<string, DateTime>();

	public static bool ShouldBlockPlayerTarget(AutoTurret turret, BasePlayer target, string context = null)
	{
		if ((Object)(object)turret == (Object)null || (Object)(object)target == (Object)null)
		{
			return false;
		}
		if (!IsRealPlayer(target))
		{
			LogDebug(turret, target, context, allow: true, "target is not a real player (npc/animal)");
			return false;
		}
		PvEOptions pve = TruePVEMod.Instance?.Config?.PvE;
		if (pve == null)
		{
			LogDebug(turret, target, context, allow: true, "PvE config missing");
			return false;
		}
		if (IsFunTurret(turret))
		{
			LogDebug(turret, target, context, allow: true, "fun turret exempt");
			return false;
		}
		if (turret is NPCAutoTurret && turret.OwnerID == 0UL)
		{
			bool block = pve.SafeZoneTurretsIgnorePlayers && target.InSafeZone();
			LogDebug(turret, target, context, !block, block ? "safe-zone npc turret" : "safe-zone flag off or victim outside safe zone");
			return block;
		}
		// RaidableBases event turrets (OwnerID 0 + RB skin) must always engage players.
		if (turret.OwnerID == 0UL && turret.skinID == 3710562502UL)
		{
			LogDebug(turret, target, context, allow: true, "raidablebases event turret");
			return false;
		}
		if (turret.OwnerID == 0UL)
		{
			bool block2 = pve.StaticTurretsIgnorePlayers;
			LogDebug(turret, target, context, !block2, block2 ? "static/monument turret" : "StaticTurretsIgnorePlayers=false");
			return block2;
		}
		if (LootDefenderHelpers.IsSteamId(turret.OwnerID))
		{
			bool block3 = pve.TurretsIgnorePlayers;
			LogDebug(turret, target, context, !block3, block3 ? "player-owned turret" : "TurretsIgnorePlayers=false");
			return block3;
		}
		LogDebug(turret, target, context, allow: true, $"turret owner not steam (owner={turret.OwnerID})");
		return false;
	}

	public static bool ShouldBlockPlayerDamage(AutoTurret turret, BasePlayer victim, string context = null)
	{
		return ShouldBlockPlayerTarget(turret, victim, context);
	}

	public static AutoTurret ResolveAutoTurret(HitInfo info)
	{
		if (info == null)
		{
			return null;
		}
		if (info.Initiator is AutoTurret initiatorTurret)
		{
			return initiatorTurret;
		}
		if (info.Weapon is BaseProjectile projectile)
		{
			BaseEntity parent = projectile.GetParentEntity();
			if (parent is AutoTurret parentTurret)
			{
				return parentTurret;
			}
		}
		BaseEntity walk = info.Initiator ?? info.Weapon as BaseEntity;
		for (int depth = 0; depth < 8 && (Object)(object)walk != (Object)null; depth++)
		{
			if (walk is AutoTurret turret)
			{
				return turret;
			}
			BaseEntity next = walk.GetParentEntity();
			if ((Object)(object)next == (Object)null || next == walk)
			{
				break;
			}
			walk = next;
		}
		return null;
	}

	public static bool TryBlockTurretPlayerDamage(BasePlayer victim, HitInfo info, string context)
	{
		AutoTurret turret = ResolveAutoTurret(info);
		if ((Object)(object)turret == (Object)null || (Object)(object)victim == (Object)null)
		{
			return false;
		}
		if (!ShouldBlockPlayerDamage(turret, victim, context))
		{
			return false;
		}
		info.damageTypes?.Clear();
		LogDebug(turret, victim, context, allow: false, "blocked damage");
		return true;
	}

	private static bool IsRealPlayer(BasePlayer player)
	{
		return LootDefenderHelpers.IsSteamId(player.userID) && !player.IsNpc;
	}

	private static bool IsFunTurret(AutoTurret turret)
	{
		try
		{
			if (turret.GetAttachedWeapon() is BaseProjectile projectile && projectile.GetItem() is Item weapon && weapon.info != null && weapon.info.shortname != null)
			{
				return weapon.info.shortname.StartsWith("fun.", StringComparison.Ordinal);
			}
		}
		catch
		{
		}
		return false;
	}

	private static void LogDebug(AutoTurret turret, BasePlayer target, string context, bool allow, string reason)
	{
		if (TruePVEMod.Instance?.Config?.PvE?.TurretIgnorePlayersDebug != true)
		{
			return;
		}
		string text = context ?? "turret";
		string key = $"{turret.net?.ID.Value}:{target.userID}:{text}:{allow}:{reason}";
		DateTime utcNow = DateTime.UtcNow;
		lock (DebugThrottle)
		{
			if (DebugThrottle.TryGetValue(key, out var value) && (utcNow - value).TotalSeconds < 1.0)
			{
				return;
			}
			DebugThrottle[key] = utcNow;
		}
		string decision = allow ? "ALLOW" : "BLOCK";
		Debug.Log((object)$"[TruePVE][TurretDebug] {decision} ctx={text} turretOwner={turret.OwnerID} victim={target.displayName} ({target.userID}) reason={reason}");
	}
}
