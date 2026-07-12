using System;
using System.Collections.Generic;
using ConVar;
using Rust;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class LootSpawn
{
	public class Entry
	{
		public LootSpawn Category;

		public int Weight;

		public int ExtraSpawns;

		public Era[] RestrictedEras = Array.Empty<Era>();
	}

	public ItemAmountRanged[] Items = Array.Empty<ItemAmountRanged>();

	public Entry[] SubSpawn = Array.Empty<Entry>();

	public byte[] Node = Array.Empty<byte>();

	private Entry[] _allowedSubSpawn;

	private ItemAmountRanged[] _allowedItems;

	private Era _era;

	public LootSpawn()
	{
	}

	public LootSpawn(global::LootSpawn gameSpawn, bool hasCondition)
	{
		global::ItemAmountRanged[] array = gameSpawn?.items;
		Items = new ItemAmountRanged[(array != null) ? array.Length : 0];
		if (array != null)
		{
			for (int i = 0; i < array.Length; i++)
			{
				global::ItemAmountRanged itemAmountRanged = array[i];
				Items[i] = ((itemAmountRanged != null) ? new ItemAmountRanged(itemAmountRanged.itemDef, itemAmountRanged.amount, itemAmountRanged.maxAmount, hasCondition) : new ItemAmountRanged());
			}
		}
		global::LootSpawn.Entry[] array2 = gameSpawn?.subSpawn;
		SubSpawn = new Entry[(array2 != null) ? array2.Length : 0];
		if (array2 != null)
		{
			for (int j = 0; j < array2.Length; j++)
			{
				global::LootSpawn.Entry entry = array2[j];
				SubSpawn[j] = new Entry
				{
					Category = (((Object)(object)entry.category != (Object)null) ? new LootSpawn(entry.category, hasCondition) : null),
					Weight = entry.weight,
					ExtraSpawns = entry.extraSpawns,
					RestrictedEras = (entry.restrictedEras ?? Array.Empty<Era>())
				};
			}
		}
	}

	private bool HasAnySpawns()
	{
		EnsureFilterUpdated();
		Entry[] allowedSubSpawn = _allowedSubSpawn;
		if (allowedSubSpawn == null || allowedSubSpawn.Length == 0)
		{
			ItemAmountRanged[] allowedItems = _allowedItems;
			return allowedItems != null && allowedItems.Length != 0;
		}
		return true;
	}

	private void EnsureFilterUpdated()
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		if (_allowedSubSpawn != null && _era == ConVar.Server.Era)
		{
			return;
		}
		_era = ConVar.Server.Era;
		if (SubSpawn == null || SubSpawn.Length == 0)
		{
			_allowedSubSpawn = Array.Empty<Entry>();
		}
		else
		{
			Entry[] subSpawn = SubSpawn;
			for (int i = 0; i < subSpawn.Length; i++)
			{
				subSpawn[i].Category?.EnsureFilterUpdated();
			}
			List<Entry> list = new List<Entry>();
			for (int j = 0; j < SubSpawn.Length; j++)
			{
				Entry entry = SubSpawn[j];
				LootSpawn category = entry.Category;
				if (category != null && category.HasAnySpawns() && (entry.RestrictedEras == null || entry.RestrictedEras.Length == 0 || Array.IndexOf(entry.RestrictedEras, ConVar.Server.Era) != -1))
				{
					list.Add(entry);
				}
			}
			_allowedSubSpawn = ((list.Count > 0) ? list.ToArray() : Array.Empty<Entry>());
		}
		if (Items == null || Items.Length == 0)
		{
			_allowedItems = Array.Empty<ItemAmountRanged>();
			return;
		}
		List<ItemAmountRanged> list2 = new List<ItemAmountRanged>();
		for (int k = 0; k < Items.Length; k++)
		{
			ItemAmountRanged itemAmountRanged = Items[k];
			if ((Object)(object)itemAmountRanged?.ItemDefinition != (Object)null && itemAmountRanged.ItemDefinition.IsAllowedInEra((EraRestriction)2))
			{
				list2.Add(itemAmountRanged);
			}
		}
		_allowedItems = ((list2.Count > 0) ? list2.ToArray() : Array.Empty<ItemAmountRanged>());
	}

	public void SpawnIntoContainer(ItemContainer container, BaseLootProfile lootProfile)
	{
		EnsureFilterUpdated();
		if (_allowedSubSpawn != null && _allowedSubSpawn.Length != 0)
		{
			SubCategoryIntoContainer(container, lootProfile);
		}
		else if (_allowedItems != null)
		{
			int itemCount = 0;
			ItemAmountRanged[] allowedItems = _allowedItems;
			for (int i = 0; i < allowedItems.Length; i++)
			{
				allowedItems[i]?.Create(container, lootProfile.LootMultiplier, lootProfile.AllowSkinnedItems, expandContainer: false, ref itemCount);
			}
		}
	}

	private void SubCategoryIntoContainer(ItemContainer container, BaseLootProfile lootProfile)
	{
		int num = 0;
		for (int i = 0; i < _allowedSubSpawn.Length; i++)
		{
			num += _allowedSubSpawn[i].Weight;
		}
		int num2 = Random.Range(0, num);
		for (int j = 0; j < _allowedSubSpawn.Length; j++)
		{
			if (_allowedSubSpawn[j].Category == null)
			{
				continue;
			}
			num -= _allowedSubSpawn[j].Weight;
			if (num2 >= num)
			{
				for (int k = 0; k < 1 + _allowedSubSpawn[j].ExtraSpawns; k++)
				{
					_allowedSubSpawn[j].Category.SpawnIntoContainer(container, lootProfile);
				}
				break;
			}
		}
	}
}
