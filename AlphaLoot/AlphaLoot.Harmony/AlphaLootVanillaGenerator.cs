using System;
using System.Collections.Generic;
using System.Reflection;
using Rust.Ai.Gen2;
using UnityEngine;

namespace AlphaLoot.Harmony;

public static class AlphaLootVanillaGenerator
{
	private static readonly FieldInfo ScientistDeadLootSpawnSlotsField = typeof(State_Dead).GetField("LootSpawnSlots", BindingFlags.Instance | BindingFlags.NonPublic);

	private const string HELI_CRATE = "heli_crate";

	private const string BRADLEY_CRATE = "bradley_crate";

	private const string UNDERWATER_LABS = "underwater_labs/";

	private const string SUPPLY_DROP_PROFILE = "supply_drop";

	public static string ToProfileName(LootContainer container)
	{
		if ((Object)(object)container == (Object)null)
		{
			return "";
		}
		if (container is SupplyDrop)
		{
			return "supply_drop";
		}
		string prefabName = container.PrefabName;
		if (prefabName != null && prefabName.Contains("underwater_labs/"))
		{
			return "underwater_labs/" + container.ShortPrefabName;
		}
		return container.ShortPrefabName ?? "";
	}

	public static int PopulateContainerDefinitions(ref StoredData storedData, ref StoredData heliData, ref StoredData bradleyData)
	{
		if (storedData == null)
		{
			storedData = new StoredData();
		}
		if (heliData == null)
		{
			heliData = new StoredData();
		}
		if (bradleyData == null)
		{
			bradleyData = new StoredData();
		}
		StoredData storedData2 = heliData;
		if (storedData2.loot_advanced == null)
		{
			storedData2.loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = heliData;
		if (storedData2.loot_simple == null)
		{
			storedData2.loot_simple = new Dictionary<string, SimpleLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = bradleyData;
		if (storedData2.loot_advanced == null)
		{
			storedData2.loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = bradleyData;
		if (storedData2.loot_simple == null)
		{
			storedData2.loot_simple = new Dictionary<string, SimpleLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = storedData;
		if (storedData2.loot_advanced == null)
		{
			storedData2.loot_advanced = new Dictionary<string, AdvancedLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = storedData;
		if (storedData2.loot_simple == null)
		{
			storedData2.loot_simple = new Dictionary<string, SimpleLootContainerProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = storedData;
		if (storedData2.npcs_advanced == null)
		{
			storedData2.npcs_advanced = new Dictionary<string, AdvancedNPCLootProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = storedData;
		if (storedData2.npcs_simple == null)
		{
			storedData2.npcs_simple = new Dictionary<string, SimpleNPCLootProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = storedData;
		if (storedData2.custom_advanced == null)
		{
			storedData2.custom_advanced = new Dictionary<string, AdvancedCustomLootProfile>(StringComparer.OrdinalIgnoreCase);
		}
		storedData2 = storedData;
		if (storedData2.custom_simple == null)
		{
			storedData2.custom_simple = new Dictionary<string, SimpleCustomLootProfile>(StringComparer.OrdinalIgnoreCase);
		}
		int num = 0;
		Dictionary<string, Object> dictionary = FileSystem.Backend?.cache;
		if (dictionary != null)
		{
			foreach (KeyValuePair<string, Object> item in dictionary)
			{
				Object value = item.Value;
				GameObject val = (GameObject)(object)((value is GameObject) ? value : null);
				if (val == null)
				{
					continue;
				}
				LootContainer component = val.GetComponent<LootContainer>();
				if ((Object)(object)component != (Object)null)
				{
					if (CreateLootDefinitionFor(component, ref storedData, ref heliData, ref bradleyData))
					{
						num++;
					}
					continue;
				}
				LootFill component2 = val.GetComponent<LootFill>();
				if ((Object)(object)component2 != (Object)null)
				{
					BaseEntity component3 = ((Component)component2).GetComponent<BaseEntity>();
					StorageContainer storageContainer = component2.StorageContainer;
					if (storedData.CreateLootFillProfile(component2, component3, storageContainer))
					{
						num++;
					}
					continue;
				}
				NPCPlayer component4 = val.GetComponent<NPCPlayer>();
				if ((Object)(object)component4 != (Object)null)
				{
					if (CreateLootDefinitionFor(component4, ref storedData))
					{
						num++;
					}
					continue;
				}
				ScientistNPC2 component5 = val.GetComponent<ScientistNPC2>();
				if ((Object)(object)component5 != (Object)null && CreateLootDefinitionFor(component5, ref storedData))
				{
					num++;
				}
			}
		}
		if (ItemManager.itemList != null)
		{
			foreach (ItemDefinition item2 in ItemManager.itemList)
			{
				if (!((Object)(object)item2 == (Object)null))
				{
					ItemModUnwrap componentInChildren = ((Component)item2).GetComponentInChildren<ItemModUnwrap>();
					if ((Object)(object)componentInChildren != (Object)null && storedData.CreateDefaultLootProfile(item2, componentInChildren))
					{
						num++;
					}
				}
			}
		}
		if (num > 0)
		{
			Debug.Log((object)$"[AlphaLoot.Harmony] Auto-update: Added {num} new loot definition(s) to profiles. Check HarmonyData/AlphaLoot/LootProfiles/ and adjust if needed.");
		}
		return num;
	}

	private static bool CreateLootDefinitionFor(LootContainer lootContainer, ref StoredData storedData, ref StoredData heliData, ref StoredData bradleyData)
	{
		string text = ToProfileName(lootContainer);
		if (string.IsNullOrEmpty(text))
		{
			text = ((Object)lootContainer).name;
		}
		if (text == "heli_crate")
		{
			if (storedData.TryGetLootProfile("heli_crate", out var profile))
			{
				heliData.CloneLootProfile("heli_crate", profile);
				storedData.RemoveProfile("heli_crate");
				return false;
			}
			if (!heliData.HasAnyProfiles)
			{
				return heliData.CreateDefaultLootProfile(lootContainer);
			}
			return false;
		}
		if (text == "bradley_crate")
		{
			if (storedData.TryGetLootProfile("bradley_crate", out var profile2))
			{
				bradleyData.CloneLootProfile("bradley_crate", profile2);
				storedData.RemoveProfile("bradley_crate");
				return false;
			}
			if (!bradleyData.HasAnyProfiles)
			{
				return bradleyData.CreateDefaultLootProfile(lootContainer);
			}
			return false;
		}
		return storedData.CreateDefaultLootProfile(lootContainer);
	}

	private static bool CreateLootDefinitionFor(NPCPlayer npcPlayer, ref StoredData storedData)
	{
		if (npcPlayer is HumanNPC humanNPC)
		{
			return storedData.CreateDefaultLootProfile(npcPlayer.ShortPrefabName, humanNPC.LootSpawnSlots, npcPlayer.loadouts);
		}
		if (npcPlayer is ScarecrowNPC scarecrowNPC)
		{
			return storedData.CreateDefaultLootProfile(npcPlayer.ShortPrefabName, scarecrowNPC.LootSpawnSlots, npcPlayer.loadouts);
		}
		return false;
	}

	private static bool CreateLootDefinitionFor(ScientistNPC2 scientistNpc, ref StoredData storedData)
	{
		if ((Object)(object)scientistNpc == (Object)null)
		{
			return false;
		}
		State_ScientistDead state_ScientistDead = ((Component)scientistNpc).GetComponent<Scientist2FSM>()?.dead;
		if (state_ScientistDead == null)
		{
			return false;
		}
		if (!(ScientistDeadLootSpawnSlotsField?.GetValue(state_ScientistDead) is LootContainer.LootSpawnSlot[] array) || array.Length == 0)
		{
			return false;
		}
		return storedData.CreateDefaultLootProfile(scientistNpc.ShortPrefabName, array, null);
	}
}
