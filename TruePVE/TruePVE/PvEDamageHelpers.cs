namespace TruePVE;

public static class PvEDamageHelpers
{
	/// <summary>
	/// Resolves the real player behind weapon/fire damage. FireBall often sets <see cref="HitInfo.Initiator"/>
	/// to the fire entity when <see cref="BaseEntity.creatorEntity"/> was not propagated (catapult incendiary, spread, etc.).
	/// </summary>
	public static BasePlayer ResolvePlayerInitiator(HitInfo info)
	{
		if (info == null)
		{
			return null;
		}
		BasePlayer direct = info.InitiatorPlayer;
		if ((Object)(object)direct != (Object)null && LootDefenderHelpers.IsSteamId(direct.userID) && !direct.IsNpc)
		{
			return direct;
		}
		BaseEntity walk = info.Initiator;
		for (int depth = 0; depth < 8 && (Object)(object)walk != (Object)null; depth++)
		{
			if (walk is BasePlayer player && LootDefenderHelpers.IsSteamId(player.userID) && !player.IsNpc)
			{
				return player;
			}
			BaseEntity next = walk.creatorEntity;
			if ((Object)(object)next == (Object)null || next == walk)
			{
				break;
			}
			walk = next;
		}
		if ((Object)(object)info.Initiator != (Object)null && info.Initiator.OwnerID != 0UL && LootDefenderHelpers.IsSteamId(info.Initiator.OwnerID))
		{
			BasePlayer byOwner = BasePlayer.FindByID(info.Initiator.OwnerID);
			if ((Object)(object)byOwner != (Object)null && !byOwner.IsNpc)
			{
				return byOwner;
			}
		}
		return null;
	}

	public static ulong ResolvePlayerInitiatorId(HitInfo info)
	{
		BasePlayer player = ResolvePlayerInitiator(info);
		if ((Object)(object)player != (Object)null)
		{
			return player.userID;
		}
		BaseEntity walk = info?.Initiator;
		for (int depth = 0; depth < 8 && (Object)(object)walk != (Object)null; depth++)
		{
			if (walk.OwnerID != 0UL && LootDefenderHelpers.IsSteamId(walk.OwnerID))
			{
				return walk.OwnerID;
			}
			BaseEntity next = walk.creatorEntity;
			if ((Object)(object)next == (Object)null || next == walk)
			{
				break;
			}
			walk = next;
		}
		return 0UL;
	}

	public static bool ShouldBlockPlayerDeployableDamage(BaseCombatEntity target, HitInfo info)
	{
		if ((Object)(object)target == (Object)null || info == null || target is BasePlayer || target is BuildingBlock)
		{
			return false;
		}
		if (info.damageTypes?.Has(Rust.DamageType.Heat) != true)
		{
			return false;
		}
		if (TruePVEMod.Instance?.Config?.PvE?.EnableGamePvE != true)
		{
			return false;
		}
		ulong attackerId = ResolvePlayerInitiatorId(info);
		if (attackerId == 0UL || target.OwnerID == 0UL || target.OwnerID == attackerId)
		{
			return false;
		}
		return IsDeployableEntity(target);
	}

	private static bool IsDeployableEntity(BaseEntity entity)
	{
		try
		{
			if ((Object)(object)entity.GetComponent<Deployable>() != (Object)null)
			{
				return true;
			}
			return PrefabAttribute.server.Find<Deployable>(entity.prefabID) != null;
		}
		catch
		{
			return false;
		}
	}
}
