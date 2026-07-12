using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class StoredData
{
	public Dictionary<string, SimpleLootContainerProfile> loot_simple = new Dictionary<string, SimpleLootContainerProfile>(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, AdvancedLootContainerProfile> loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, AdvancedNPCLootProfile> npcs_advanced = new Dictionary<string, AdvancedNPCLootProfile>(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, SimpleNPCLootProfile> npcs_simple = new Dictionary<string, SimpleNPCLootProfile>(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, AdvancedCustomLootProfile> custom_advanced = new Dictionary<string, AdvancedCustomLootProfile>(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, SimpleCustomLootProfile> custom_simple = new Dictionary<string, SimpleCustomLootProfile>(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, List<string>> npc_loadouts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	private List<BaseLootContainerProfile> _randomList = new List<BaseLootContainerProfile>();

	public bool HasAnyProfiles
	{
		get
		{
			if ((loot_advanced?.Count ?? 0) <= 0 && (loot_simple?.Count ?? 0) <= 0 && (npcs_advanced?.Count ?? 0) <= 0 && (npcs_simple?.Count ?? 0) <= 0 && (custom_advanced?.Count ?? 0) <= 0)
			{
				return (custom_simple?.Count ?? 0) > 0;
			}
			return true;
		}
	}

	public bool CreateDefaultLootProfile(LootContainer container)
	{
		string text = AlphaLootVanillaGenerator.ToProfileName(container);
		if (string.IsNullOrEmpty(text))
		{
			text = ((Object)container).name;
		}
		Dictionary<string, AdvancedLootContainerProfile> dictionary = loot_advanced;
		if (dictionary == null || !dictionary.ContainsKey(text))
		{
			Dictionary<string, SimpleLootContainerProfile> dictionary2 = loot_simple;
			if (dictionary2 == null || !dictionary2.ContainsKey(text))
			{
				if (loot_advanced == null)
				{
					loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
				}
				loot_advanced.Add(text, new AdvancedLootContainerProfile(container));
				return true;
			}
		}
		return false;
	}

	public bool CreateDefaultLootProfile(ItemDefinition itemDef, ItemModUnwrap itemModUnwrap)
	{
		if (!((Object)(object)itemDef == (Object)null))
		{
			Dictionary<string, AdvancedLootContainerProfile> dictionary = loot_advanced;
			if (dictionary == null || !dictionary.ContainsKey(itemDef.shortname))
			{
				Dictionary<string, SimpleLootContainerProfile> dictionary2 = loot_simple;
				if (dictionary2 == null || !dictionary2.ContainsKey(itemDef.shortname))
				{
					if (loot_advanced == null)
					{
						loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
					}
					loot_advanced.Add(itemDef.shortname, new AdvancedLootContainerProfile(itemModUnwrap));
					return true;
				}
			}
		}
		return false;
	}

	public bool CreateLootFillProfile(LootFill lootFill, BaseEntity entity, StorageContainer container)
	{
		if ((Object)(object)lootFill == (Object)null || (Object)(object)entity == (Object)null || (Object)(object)container == (Object)null)
		{
			return false;
		}
		string key = AlphaLootMod.ToLootFillProfileName(entity, container);
		Dictionary<string, AdvancedLootContainerProfile> dictionary = loot_advanced;
		if (dictionary == null || !dictionary.ContainsKey(key))
		{
			Dictionary<string, SimpleLootContainerProfile> dictionary2 = loot_simple;
			if (dictionary2 == null || !dictionary2.ContainsKey(key))
			{
				if (loot_advanced == null)
				{
					loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
				}
				loot_advanced.Add(key, new AdvancedLootContainerProfile(lootFill));
				return true;
			}
		}
		return false;
	}

	public bool CreateDefaultLootProfile(string shortPrefabName, LootContainer.LootSpawnSlot[] slots, PlayerInventoryProperties[] loadouts)
	{
		if (npc_loadouts == null)
		{
			npc_loadouts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		}
		if (!npc_loadouts.TryGetValue(shortPrefabName, out var value))
		{
			value = (npc_loadouts[shortPrefabName] = new List<string>());
		}
		if (loadouts != null)
		{
			foreach (PlayerInventoryProperties playerInventoryProperties in loadouts)
			{
				if (playerInventoryProperties != null && !value.Contains(playerInventoryProperties.niceName))
				{
					value.Add(playerInventoryProperties.niceName);
				}
			}
		}
		Dictionary<string, AdvancedNPCLootProfile> dictionary = npcs_advanced;
		if (dictionary == null || !dictionary.ContainsKey(shortPrefabName))
		{
			Dictionary<string, SimpleNPCLootProfile> dictionary2 = npcs_simple;
			if (dictionary2 == null || !dictionary2.ContainsKey(shortPrefabName))
			{
				if (npcs_advanced == null)
				{
					npcs_advanced = new Dictionary<string, AdvancedNPCLootProfile>(StringComparer.OrdinalIgnoreCase);
				}
				npcs_advanced.Add(shortPrefabName, new AdvancedNPCLootProfile(slots));
				return true;
			}
		}
		return false;
	}

	public void CloneLootProfile(string shortname, BaseLootProfile source)
	{
		if (source is AdvancedLootContainerProfile copy)
		{
			if (loot_advanced == null)
			{
				loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
			}
			loot_advanced[shortname] = new AdvancedLootContainerProfile(copy);
		}
	}

	public void RemoveProfile(string shortname)
	{
		loot_simple?.Remove(shortname);
		loot_advanced?.Remove(shortname);
		npcs_simple?.Remove(shortname);
		npcs_advanced?.Remove(shortname);
	}

	public bool TryGetLootProfile(string shortname, out BaseLootContainerProfile profile)
	{
		if (loot_advanced.TryGetValue(shortname, out var value))
		{
			profile = value;
			return true;
		}
		if (loot_simple.TryGetValue(shortname, out var value2))
		{
			profile = value2;
			return true;
		}
		profile = null;
		return false;
	}

	public bool TryGetNPCProfile(string shortname, out BaseLootProfile profile)
	{
		if (npcs_advanced.TryGetValue(shortname, out var value))
		{
			profile = value;
			return true;
		}
		if (npcs_simple.TryGetValue(shortname, out var value2))
		{
			profile = value2;
			return true;
		}
		profile = null;
		return false;
	}

	public bool TryGetCustomProfile(string shortname, out BaseLootProfile profile)
	{
		if (custom_advanced.TryGetValue(shortname, out var value))
		{
			profile = value;
			return true;
		}
		if (custom_simple.TryGetValue(shortname, out var value2))
		{
			profile = value2;
			return true;
		}
		profile = null;
		return false;
	}

	public bool GetRandomLootProfile(out BaseLootContainerProfile profile)
	{
		if (_randomList.Count == 0)
		{
			_randomList.AddRange(loot_simple.Values);
			_randomList.AddRange(loot_advanced.Values);
		}
		do
		{
			if (_randomList.Count == 0)
			{
				profile = null;
				return false;
			}
			profile = _randomList[Random.Range(0, _randomList.Count)];
			_randomList.Remove(profile);
		}
		while (!profile.Enabled);
		return true;
	}
}
