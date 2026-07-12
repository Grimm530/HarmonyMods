namespace TruePVE;

public static class PvEDamageHelpers
{
	private static readonly System.Collections.Generic.HashSet<uint> ProtectedPrefabIds = new System.Collections.Generic.HashSet<uint>();

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

	public static bool ShouldBlockPlayerOwnedEntityDamage(BaseCombatEntity target, HitInfo info, out BasePlayer attackerPlayer)
	{
		attackerPlayer = null;
		if ((Object)(object)target == (Object)null || info == null || target is BasePlayer || target is BuildingBlock)
		{
			return false;
		}
		if (TruePVEMod.Instance?.Config?.PvE?.EnableGamePvE != true)
		{
			return false;
		}
		attackerPlayer = ResolvePlayerInitiator(info);
		ulong attackerId = ResolvePlayerInitiatorId(info);
		if (attackerId == 0UL || target.OwnerID == 0UL || target.OwnerID == attackerId)
		{
			return false;
		}
		return IsProtectedPlayerEntity(target);
	}

	private static bool IsProtectedPlayerEntity(BaseEntity entity)
	{
		if ((Object)(object)entity == (Object)null)
		{
			return false;
		}
		if (entity is Door || entity is StorageContainer || entity is BuildingPrivlidge || entity is Barricade || entity is SimpleBuildingBlock)
		{
			return true;
		}
		try
		{
			if ((Object)(object)entity.GetComponent<Deployable>() != (Object)null)
			{
				return true;
			}
			if (ProtectedPrefabIds.Contains(entity.prefabID))
			{
				return true;
			}
			if (PrefabAttribute.server.Find<Deployable>(entity.prefabID) != null)
			{
				ProtectedPrefabIds.Add(entity.prefabID);
				return true;
			}
			string prefabName = entity.PrefabName ?? string.Empty;
			if (prefabName.IndexOf("building", System.StringComparison.OrdinalIgnoreCase) >= 0 || prefabName.IndexOf("modular", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			string shortPrefab = entity.ShortPrefabName ?? string.Empty;
			if (shortPrefab.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}
}
