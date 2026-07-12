using Newtonsoft.Json;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class BaseLootProfile
{
	public bool Enabled = true;

	public bool AllowSkinnedItems = true;

	public float LootMultiplier = 1f;

	public int MinScrapAmount;

	public int MaxScrapAmount;

	private static ItemDefinition _scrapDefinition;

	private static ItemDefinition _blueprintBase;

	[JsonIgnore]
	public ItemDefinition ScrapDefinition
	{
		get
		{
			if (_scrapDefinition == null)
			{
				return _scrapDefinition = ItemManager.FindItemDefinition("scrap");
			}
			return _scrapDefinition;
		}
	}

	[JsonIgnore]
	public static ItemDefinition BlueprintBaseDefinition
	{
		get
		{
			if (_blueprintBase == null)
			{
				return _blueprintBase = ItemManager.FindItemDefinition("blueprintbase");
			}
			return _blueprintBase;
		}
	}

	public int GetScrapAmount()
	{
		return Random.Range(MinScrapAmount, MaxScrapAmount);
	}

	public virtual void PopulateLoot(ItemContainer container)
	{
		if (container.playerOwner != null)
		{
			return;
		}
		AlphaLootConfig config = AlphaLootContext.Config;
		int num = Mathf.RoundToInt((float)GetScrapAmount() * (config?.GlobalMultiplier ?? 1f));
		if (num > 0)
		{
			SetContainerCapacity(container, container.itemList.Count + 1);
			if (container.entityOwner is LootContainer lootContainer)
			{
				lootContainer.scrapAmount = num;
				lootContainer.GenerateScrap();
			}
			else
			{
				ItemManager.Create(ScrapDefinition, num, 0uL, true, 0uL).MoveToContainer(container);
			}
		}
		else
		{
			SetContainerCapacity(container, container.itemList.Count);
		}
	}

	public virtual void PopulateLoot(ItemContainer itemContainer, string loadoutName)
	{
		PopulateLoot(itemContainer);
	}

	public static void SetContainerCapacity(ItemContainer container, int i)
	{
		if (container.playerOwner == null)
		{
			container.capacity = i;
		}
	}
}
