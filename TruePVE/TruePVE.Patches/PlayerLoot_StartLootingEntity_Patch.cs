using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TruePVE.Patches;

[HarmonyPatch(typeof(PlayerLoot), "StartLootingEntity", new Type[]
{
	typeof(BaseEntity),
	typeof(bool)
})]
public static class PlayerLoot_StartLootingEntity_Patch
{
	[HarmonyPrefix]
	public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, bool doPositionChecks, ref bool __result)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)targetEntity == (Object)null)
		{
			return true;
		}
		BasePlayer componentInParent = ((Component)__instance).GetComponentInParent<BasePlayer>();
		if ((Object)(object)componentInParent == (Object)null)
		{
			return true;
		}
		TruePVEMod instance = TruePVEMod.Instance;
		if (instance?.Config == null)
		{
			return true;
		}
		if (instance.Config.LootDefender.Enabled && instance.Config.LootDefender.BlockLootingOnly)
		{
			bool skipPositionLock = LootDefenderHelpers.IsNpcCorpse(targetEntity);
			if (!skipPositionLock && LootDefenderState.IsPositionLocked(((Component)targetEntity).transform.position, componentInParent.userID, out var isOwnerOrAlly))
			{
				return Block(instance, componentInParent, targetEntity, "loot defender position lock", ref __result);
			}
			if (targetEntity.net != null && LootDefenderState.IsEntityLocked(targetEntity.net.ID.Value, componentInParent.userID, out var isOwnerOrAlly2))
			{
				return Block(instance, componentInParent, targetEntity, "loot defender entity lock", ref __result);
			}
		}
		if (!instance.Config.PreventLooting.Enabled)
		{
			return Allow(instance, componentInParent, targetEntity, "prevent looting disabled");
		}
		if (targetEntity is BuildingPrivlidge && !instance.CanAccessToolCupboard(componentInParent, targetEntity))
		{
			return Block(instance, componentInParent, targetEntity, "tool cupboard owner or teammate required", ref __result);
		}
		if (instance.Config.PreventLooting.AdminCanLoot && TruePVEMod.IsAdminOrDeveloperLooter(componentInParent))
		{
			return Allow(instance, componentInParent, targetEntity, "admin override");
		}
		if (instance.Config.PreventLooting.ExcludedShortPrefabNames != null || instance.Config.PreventLooting.ExcludeEntities != null)
		{
			string item = targetEntity.ShortPrefabName ?? "";
			List<string> excludedShortPrefabNames = instance.Config.PreventLooting.ExcludedShortPrefabNames;
			if (excludedShortPrefabNames == null || !excludedShortPrefabNames.Contains(item))
			{
				List<string> excludeEntities = instance.Config.PreventLooting.ExcludeEntities;
				if (excludeEntities == null || !excludeEntities.Contains(item))
				{
					goto IL_0170;
				}
			}
			return Allow(instance, componentInParent, targetEntity, "excluded prefab");
		}
		goto IL_0170;
		IL_0170:
		if (targetEntity.OwnerID != 0L && instance.IsAlly(targetEntity.OwnerID, componentInParent.userID))
		{
			return Allow(instance, componentInParent, targetEntity, "owner or ally");
		}
		if (LootDefenderHelpers.IsNpcCorpse(targetEntity))
		{
			return Allow(instance, componentInParent, targetEntity, "npc corpse");
		}
		if (targetEntity is BasePlayer basePlayer)
		{
			if (basePlayer.IsSleeping())
			{
				if (!instance.Config.PreventLooting.AllowLootingSleepers && !instance.IsAlly(basePlayer.userID, componentInParent.userID))
				{
					return Block(instance, componentInParent, targetEntity, "sleeping player access denied", ref __result);
				}
				return Allow(instance, componentInParent, targetEntity, "sleeping player access allowed");
			}
			else if (!instance.Config.PreventLooting.AllowLootingPlayers)
			{
				return Block(instance, componentInParent, targetEntity, "player looting disabled", ref __result);
			}
			else
			{
				return Allow(instance, componentInParent, targetEntity, "player looting allowed by config");
			}
		}
		else if (targetEntity is LootableCorpse || targetEntity is PlayerCorpse)
		{
			ulong num = targetEntity.OwnerID;
			if (num == 0L && targetEntity is PlayerCorpse playerCorpse)
			{
				num = playerCorpse.playerSteamID;
			}
			if (num != 0L && !instance.IsAlly(num, componentInParent.userID) && !instance.Config.PreventLooting.AllowLootingCorpses)
			{
				return Block(instance, componentInParent, targetEntity, "corpse looting denied", ref __result);
			}
			return Allow(instance, componentInParent, targetEntity, num == 0L ? "unowned or npc corpse" : "corpse owner or ally/config allowed");
		}
		else if (targetEntity is DroppedItemContainer droppedItemContainer)
		{
			if (LootDefenderHelpers.IsNpcBackpack(targetEntity))
			{
				return Allow(instance, componentInParent, targetEntity, "npc backpack");
			}
			if (!instance.Config.PreventLooting.CanLootBackpack)
			{
				ulong playerSteamID = droppedItemContainer.playerSteamID;
				if (playerSteamID != 0L && playerSteamID != (ulong)componentInParent.userID && !instance.IsAlly(playerSteamID, componentInParent.userID))
				{
					return Block(instance, componentInParent, targetEntity, "backpack looting denied", ref __result);
				}
			}
			return Allow(instance, componentInParent, targetEntity, "backpack owner, ally, unowned, or config allowed");
		}
		else if ((targetEntity is BuildingPrivlidge || targetEntity is BaseOven || targetEntity is StorageContainer || targetEntity is ContainerIOEntity || targetEntity is IndustrialCrafter) && !instance.Config.PreventLooting.AllowLootingStorageContainers && targetEntity.OwnerID != 0L && !instance.ShouldAllowStorageAccess(componentInParent, targetEntity))
		{
			return Block(instance, componentInParent, targetEntity, "storage access denied", ref __result);
		}
		if (targetEntity is BuildingPrivlidge || targetEntity is BaseOven || targetEntity is StorageContainer || targetEntity is ContainerIOEntity || targetEntity is IndustrialCrafter)
		{
			return Allow(instance, componentInParent, targetEntity, "storage owner, ally, cupboard auth, unowned, or config allowed");
		}
		return Allow(instance, componentInParent, targetEntity, "entity type not restricted by prevent looting");
	}

	private static bool Allow(TruePVEMod instance, BasePlayer looter, BaseEntity target, string reason)
	{
		instance.LogLootAllowed(looter, target, reason);
		return true;
	}

	private static bool Block(TruePVEMod instance, BasePlayer looter, BaseEntity target, string reason, ref bool result)
	{
		instance.LogLootBlocked(looter, target, reason);
		result = false;
		return false;
	}
}
