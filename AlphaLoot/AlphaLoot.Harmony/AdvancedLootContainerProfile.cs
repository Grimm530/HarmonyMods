using System;
using ConVar;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class AdvancedLootContainerProfile : BaseLootContainerProfile
{
	public LootSpawnSlot[] LootSpawnSlots;

	public int MaximumItems = -1;

	public AdvancedLootContainerProfile()
	{
	}

	public AdvancedLootContainerProfile(LootContainer container)
	{
		bool hasCondition = container.SpawnType == LootContainer.spawnType.ROADSIDE || container.SpawnType == LootContainer.spawnType.TOWN;
		DestroyOnEmpty = container.destroyOnEmpty;
		ShouldRefreshContents = !float.IsInfinity(container.minSecondsBetweenRefresh) && !float.IsInfinity(container.maxSecondsBetweenRefresh) && container.shouldRefreshContents;
		int num = container.scrapAmount;
		if (num <= 0)
		{
			num = 1;
		}
		MinScrapAmount = (MaxScrapAmount = num);
		MinSecondsBetweenRefresh = (ShouldRefreshContents ? Mathf.RoundToInt(container.minSecondsBetweenRefresh) : 0);
		MaxSecondsBetweenRefresh = (ShouldRefreshContents ? Mathf.RoundToInt(container.maxSecondsBetweenRefresh) : 0);
		MaximumItems = container.inventorySlots;
		LootContainer.LootSpawnSlot[] lootSpawnSlots = container.LootSpawnSlots;
		if (lootSpawnSlots != null && lootSpawnSlots.Length != 0)
		{
			LootSpawnSlots = new LootSpawnSlot[lootSpawnSlots.Length];
			for (int i = 0; i < lootSpawnSlots.Length; i++)
			{
				LootSpawnSlots[i] = new LootSpawnSlot(lootSpawnSlots[i], hasCondition);
			}
		}
		else if (container.lootDefinition != null)
		{
			LootSpawnSlots = new LootSpawnSlot[1]
			{
				new LootSpawnSlot(container.lootDefinition, container.maxDefinitionsToSpawn, hasCondition)
			};
		}
	}

	public AdvancedLootContainerProfile(ItemModUnwrap itemModUnwrap)
	{
		MaximumItems = -1;
		IsItemLoot = true;
		LootSpawnSlots = new LootSpawnSlot[1]
		{
			new LootSpawnSlot(itemModUnwrap.revealList, 1, hasCondition: false)
		};
	}

	public AdvancedLootContainerProfile(LootFill lootFill)
	{
		MaximumItems = -1;
		IsLootFill = true;
		LootContainer.LootSpawnSlot[] array = lootFill?.LootSpawnSlots;
		if (array != null && array.Length != 0)
		{
			LootSpawnSlots = new LootSpawnSlot[array.Length];
			for (int i = 0; i < array.Length; i++)
			{
				LootSpawnSlots[i] = new LootSpawnSlot(array[i], hasCondition: false);
			}
		}
		else if ((Object)(object)lootFill?.LootDefinition != (Object)null)
		{
			LootSpawnSlots = new LootSpawnSlot[1]
			{
				new LootSpawnSlot(lootFill.LootDefinition, lootFill.MaxDefinitionsToSpawn, hasCondition: false)
			};
		}
		else
		{
			LootSpawnSlots = Array.Empty<LootSpawnSlot>();
		}
	}

	public AdvancedLootContainerProfile(AdvancedLootContainerProfile copy)
	{
		if (copy != null)
		{
			DestroyOnEmpty = copy.DestroyOnEmpty;
			AllowSkinnedItems = copy.AllowSkinnedItems;
			ShouldRefreshContents = copy.ShouldRefreshContents;
			MinSecondsBetweenRefresh = copy.MinSecondsBetweenRefresh;
			MaxSecondsBetweenRefresh = copy.MaxSecondsBetweenRefresh;
			MinScrapAmount = copy.MinScrapAmount;
			MaxScrapAmount = copy.MaxScrapAmount;
			MaximumItems = copy.MaximumItems;
			IsItemLoot = copy.IsItemLoot;
			IsLootFill = copy.IsLootFill;
			LootSpawnSlots = copy.LootSpawnSlots;
			Enabled = copy.Enabled;
		}
	}

	public override void PopulateLoot(ItemContainer container)
	{
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		if (LootSpawnSlots != null && LootSpawnSlots.Length != 0)
		{
			BaseLootProfile.SetContainerCapacity(container, (MaximumItems == -1) ? 36 : MaximumItems);
			LootSpawnSlot[] lootSpawnSlots = LootSpawnSlots;
			foreach (LootSpawnSlot lootSpawnSlot in lootSpawnSlots)
			{
				if (lootSpawnSlot?.LootDefinition == null || (lootSpawnSlot.Eras != null && lootSpawnSlot.Eras.Length != 0 && Array.IndexOf(lootSpawnSlot.Eras, Server.Era) == -1))
				{
					continue;
				}
				for (int j = 0; j < lootSpawnSlot.NumberToSpawn; j++)
				{
					if (Random.Range(0f, 1f) <= lootSpawnSlot.Probability)
					{
						lootSpawnSlot.LootDefinition.SpawnIntoContainer(container, this);
					}
				}
			}
		}
		base.PopulateLoot(container);
	}
}
