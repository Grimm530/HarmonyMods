namespace TruePVE;

public static class LootDefenderHelpers
{
	/// <summary>Steam64 IDs are 17 digits; event/plugin NPCs use shorter fake IDs.</summary>
	public static bool IsSteamId(ulong userId) => userId > 10000000000000000UL;

	public static bool IsNpcCorpse(BaseEntity entity)
	{
		if ((Object)(object)entity == (Object)null)
		{
			return false;
		}
		if (entity is NPCPlayerCorpse)
		{
			return true;
		}
		if (entity is PlayerCorpse playerCorpse && !IsSteamId(playerCorpse.playerSteamID))
		{
			return true;
		}
		if (entity is LootableCorpse lootableCorpse && !IsSteamId(lootableCorpse.playerSteamID))
		{
			return true;
		}
		return false;
	}

	public static bool IsNpcBackpack(BaseEntity entity)
	{
		if ((Object)(object)entity == (Object)null)
		{
			return false;
		}
		if (entity is DroppedItemContainer droppedItemContainer && droppedItemContainer.playerSteamID != 0UL && !IsSteamId(droppedItemContainer.playerSteamID))
		{
			return true;
		}
		return false;
	}

	/// <summary>Match Oxide LootDefender: skip NPC locks for event/plugin-spawned NPCs.</summary>
	public static bool ShouldSkipLootDefenderNpcLock(BaseCombatEntity entity)
	{
		if ((Object)(object)entity == (Object)null)
		{
			return true;
		}
		if (entity.OwnerID != 0UL && entity.OwnerID.ToString().Length == 5)
		{
			return true;
		}
		if (entity.skinID == 11162132011012UL)
		{
			return true;
		}
		if (entity is BasePlayer basePlayer && !IsSteamId(basePlayer.userID))
		{
			return true;
		}
		if (((object)entity).GetType().Name == "ScientistNPC2")
		{
			return true;
		}
		return false;
	}
}
