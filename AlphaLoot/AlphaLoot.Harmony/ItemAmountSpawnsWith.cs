using System;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class ItemAmountSpawnsWith : ItemAmountWeighted
{
	public ItemAmountWeighted[] SpawnsWith = Array.Empty<ItemAmountWeighted>();

	public override void CreateAdditionalItems(ItemContainer container, float lootMultiplier, bool allowSkinnedItems, bool expandContainer, ref int itemCount)
	{
		if (SpawnsWith == null || SpawnsWith.Length == 0)
		{
			return;
		}
		ItemAmountWeighted[] spawnsWith = SpawnsWith;
		foreach (ItemAmountWeighted obj in spawnsWith)
		{
			if (container.playerOwner == null && expandContainer)
			{
				container.capacity++;
			}
			obj.Create(container, lootMultiplier, allowSkinnedItems, expandContainer, ref itemCount);
		}
	}
}
