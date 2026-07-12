using System.Collections.Generic;
using Facepunch;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class SimpleLootContainerProfile : BaseLootContainerProfile
{
	public int MinimumItems;

	public int MaximumItems;

	public ItemAmountSpawnsWith[] Items;

	public override void PopulateLoot(ItemContainer container)
	{
		int num = Random.Range(MinimumItems, MaximumItems + 1);
		BaseLootProfile.SetContainerCapacity(container, num);
		List<ItemAmountSpawnsWith> list = Pool.Get<List<ItemAmountSpawnsWith>>();
		list.AddRange(Items);
		int itemCount = 0;
		while (itemCount < num)
		{
			int num2 = 0;
			for (int i = 0; i < list.Count; i++)
			{
				num2 += list[i].Weight;
			}
			int num3 = Random.Range(0, num2);
			for (int j = 0; j < list.Count; j++)
			{
				ItemAmountSpawnsWith itemAmountSpawnsWith = list[j];
				num2 -= itemAmountSpawnsWith.Weight;
				if (num3 >= num2)
				{
					list.Remove(itemAmountSpawnsWith);
					itemAmountSpawnsWith.Create(container, LootMultiplier, AllowSkinnedItems, expandContainer: true, ref itemCount);
					break;
				}
			}
			if (list.Count == 0)
			{
				list.AddRange(Items);
			}
		}
		BaseLootProfile.SetContainerCapacity(container, list.Count);
		Pool.FreeUnmanaged<ItemAmountSpawnsWith>(ref list);
		base.PopulateLoot(container);
	}
}
