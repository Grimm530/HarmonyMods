using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using Arg = ConsoleSystem.Arg;

namespace AlphaLoot.Harmony;

public static class AlphaLootTools
{
	public class ItemListFile
	{
		[JsonProperty("itemIds")]
		public List<int> itemIds = new List<int>();

		[JsonProperty("protocol")]
		public string protocol;
	}

	private const string AUTOUPDATER_FILE = "AutoUpdater/do_not_edit_this_file.json";

	private static readonly string[] DefaultAddContainers = new string[4] { "crate_normal", "loot-barrel-1", "loot-barrel-2", "box.wooden" };

	public static void LogLootIfDebug(ItemContainer container, string source, string profileName, float globalMult, float profileMult)
	{
		if (container?.itemList == null)
		{
			return;
		}
		AlphaLootConfig config = AlphaLootContext.Config;
		if (config == null || !config.DebugLootTable)
		{
			return;
		}
		List<string> list = new List<string>();
		for (int i = 0; i < container.itemList.Count; i++)
		{
			Item item = container.itemList[i];
			if ((Object)(object)item?.info != (Object)null)
			{
				list.Add($"{item.info.shortname} x{item.amount}");
			}
		}
		string text = $"{globalMult * profileMult:F1}x";
		Debug.Log((object)string.Format("[AlphaLoot Debug] {0} | profile={1} | multiplier={2:F1}x global × {3:F1}x profile = {4} | items: {5}", source, profileName, globalMult, profileMult, text, string.Join(", ", list)));
	}

	public static void ClearItemContainer(ItemContainer container)
	{
		if (container?.itemList == null)
		{
			return;
		}
		while (container.itemList.Count > 0)
		{
			Item item = container.itemList[0];
			if (item != null)
			{
				item.RemoveFromContainer();
				item.Remove();
				continue;
			}
			break;
		}
	}

	public static void RunAutoUpdater(AlphaLootMod mod)
	{
		if (mod == null)
		{
			return;
		}
		string basePath = GetBasePath(mod);
		if (string.IsNullOrEmpty(basePath))
		{
			return;
		}
		string path = Path.Combine(basePath, "AutoUpdater/do_not_edit_this_file.json");
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		ItemListFile itemListFile = null;
		if (File.Exists(path))
		{
			try
			{
				itemListFile = JsonConvert.DeserializeObject<ItemListFile>(File.ReadAllText(path));
			}
			catch
			{
			}
		}
		if (itemListFile != null && itemListFile.protocol == Rust.Protocol.printable)
		{
			Debug.Log((object)"[AlphaLoot Auto Updater] Protocol matches. No new items added.");
			return;
		}
		List<int> list = new List<int>();
		if (ItemManager.itemDictionary != null)
		{
			foreach (int key in ItemManager.itemDictionary.Keys)
			{
				list.Add(key);
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		if (itemListFile == null || itemListFile.itemIds == null)
		{
			itemListFile = new ItemListFile
			{
				itemIds = new List<int>(),
				protocol = ""
			};
		}
		List<int> list2 = new List<int>();
		for (int i = 0; i < list.Count; i++)
		{
			if (!itemListFile.itemIds.Contains(list[i]))
			{
				list2.Add(list[i]);
			}
		}
		if (list2.Count > 0)
		{
			Debug.Log((object)$"[AlphaLoot Auto Updater] Found {list2.Count} new items. Adding to loot tables.");
			AddItemsToLootTable(mod, list2, out var additions);
			if (additions > 0)
			{
				SaveData(mod);
				Debug.Log((object)$"[AlphaLoot Auto Updater] Added {additions} loot definitions.");
			}
		}
		File.WriteAllText(path, JsonConvert.SerializeObject((object)new ItemListFile
		{
			itemIds = list,
			protocol = Rust.Protocol.printable
		}, (Formatting)1));
	}

	public static void AddItems(Arg arg, string[] shortnames, AlphaLootMod mod)
	{
		if (mod == null)
		{
			Reply(arg, "AlphaLoot mod not loaded.");
			return;
		}
		if (shortnames == null || shortnames.Length == 0)
		{
			Reply(arg, "al.additems <shortname> [shortname2] ... - Add item(s) to loot tables.");
			return;
		}
		List<int> list = new List<int>();
		for (int i = 0; i < shortnames.Length; i++)
		{
			ItemDefinition itemDefinition = ItemManager.FindItemDefinition(shortnames[i]);
			if ((Object)(object)itemDefinition != (Object)null)
			{
				list.Add(itemDefinition.itemid);
			}
		}
		if (list.Count == 0)
		{
			Reply(arg, "No valid item shortnames found.");
			return;
		}
		AddItemsToLootTable(mod, list, out var additions);
		if (additions > 0)
		{
			SaveData(mod);
			Reply(arg, $"Added {additions} loot definitions for {list.Count} items.");
		}
		else
		{
			Reply(arg, "No matching containers found to add items to.");
		}
	}

	public static void SearchItem(Arg arg, string shortname, AlphaLootMod mod)
	{
		if (mod == null)
		{
			Reply(arg, "AlphaLoot mod not loaded.");
			return;
		}
		if (string.IsNullOrEmpty(shortname))
		{
			Reply(arg, "al.search <shortname> - Search which containers have this item.");
			return;
		}
		if (!ItemManager.itemDictionaryByName.ContainsKey(shortname))
		{
			Reply(arg, "Invalid item shortname.");
			return;
		}
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		SearchInProfile(mod.StoredData, shortname, dictionary);
		SearchInProfile(mod.HeliData, shortname, dictionary);
		SearchInProfile(mod.BradleyData, shortname, dictionary);
		if (dictionary.Count == 0)
		{
			Reply(arg, "Item '" + shortname + "' not found in any loot profile.");
			return;
		}
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, int> item in dictionary)
		{
			list.Add($"{item.Key}:{item.Value}");
		}
		Reply(arg, string.Join(", ", list));
	}

	private static void SearchInProfile(StoredData data, string shortname, Dictionary<string, int> results)
	{
		if (data == null)
		{
			return;
		}
		foreach (KeyValuePair<string, AdvancedLootContainerProfile> item in data.loot_advanced ?? new Dictionary<string, AdvancedLootContainerProfile>())
		{
			int count = 0;
			CountItemInSlots(item.Value?.LootSpawnSlots, shortname, ref count);
			if (count > 0)
			{
				results[item.Key] = (results.TryGetValue(item.Key, out var value) ? value : 0) + count;
			}
		}
		foreach (KeyValuePair<string, SimpleLootContainerProfile> item2 in data.loot_simple ?? new Dictionary<string, SimpleLootContainerProfile>())
		{
			int count2 = 0;
			CountItemInItems(item2.Value?.Items, shortname, ref count2);
			if (count2 > 0)
			{
				results[item2.Key] = (results.TryGetValue(item2.Key, out var value2) ? value2 : 0) + count2;
			}
		}
		foreach (KeyValuePair<string, AdvancedNPCLootProfile> item3 in data.npcs_advanced ?? new Dictionary<string, AdvancedNPCLootProfile>())
		{
			int count3 = 0;
			CountItemInSlots(item3.Value?.LootSpawnSlots, shortname, ref count3);
			if (count3 > 0)
			{
				results[item3.Key] = (results.TryGetValue(item3.Key, out var value3) ? value3 : 0) + count3;
			}
		}
		foreach (KeyValuePair<string, SimpleNPCLootProfile> item4 in data.npcs_simple ?? new Dictionary<string, SimpleNPCLootProfile>())
		{
			int count4 = 0;
			CountItemInItems(item4.Value?.Items, shortname, ref count4);
			if (count4 > 0)
			{
				results[item4.Key] = (results.TryGetValue(item4.Key, out var value4) ? value4 : 0) + count4;
			}
		}
	}

	private static void CountItemInSlots(LootSpawnSlot[] slots, string shortname, ref int count)
	{
		if (slots != null)
		{
			for (int i = 0; i < slots.Length; i++)
			{
				CountItemInLootSpawn(slots[i]?.LootDefinition, shortname, ref count);
			}
		}
	}

	private static void CountItemInLootSpawn(LootSpawn spawn, string shortname, ref int count)
	{
		if (spawn == null)
		{
			return;
		}
		if (spawn.Items != null)
		{
			ItemAmountRanged[] items = spawn.Items;
			for (int i = 0; i < items.Length; i++)
			{
				if (items[i]?.Shortname == shortname)
				{
					count++;
				}
			}
		}
		if (spawn.SubSpawn != null)
		{
			LootSpawn.Entry[] subSpawn = spawn.SubSpawn;
			for (int i = 0; i < subSpawn.Length; i++)
			{
				CountItemInLootSpawn(subSpawn[i]?.Category, shortname, ref count);
			}
		}
	}

	private static void CountItemInItems(ItemAmountSpawnsWith[] items, string shortname, ref int count)
	{
		if (items == null)
		{
			return;
		}
		for (int i = 0; i < items.Length; i++)
		{
			if (items[i]?.Shortname == shortname)
			{
				count++;
			}
		}
	}

	private static void AddItemsToLootTable(AlphaLootMod mod, List<int> itemIds, out int additions)
	{
		additions = 0;
		AdvancedLootContainerProfile value2 = default(AdvancedLootContainerProfile);
		foreach (int itemId in itemIds)
		{
			if (!ItemManager.itemDictionary.TryGetValue(itemId, out var value) || (Object)(object)value == (Object)null)
			{
				continue;
			}
			string shortname = value.shortname;
			string[] defaultAddContainers = DefaultAddContainers;
			foreach (string text in defaultAddContainers)
			{
				StoredData storedData = mod.StoredData;
				if (storedData != null && storedData.loot_advanced?.TryGetValue(text, out value2) == true && AddItemToProfile(value2, shortname, text))
				{
					additions++;
				}
			}
		}
	}

	private static bool AddItemToProfile(AdvancedLootContainerProfile profile, string shortname, string containerName)
	{
		if (profile?.LootSpawnSlots == null)
		{
			return false;
		}
		LootSpawnSlot[] lootSpawnSlots = profile.LootSpawnSlots;
		for (int i = 0; i < lootSpawnSlots.Length; i++)
		{
			if (SlotContainsItem(lootSpawnSlots[i], shortname))
			{
				return false;
			}
		}
		LootSpawnSlot lootSpawnSlot = new LootSpawnSlot();
		lootSpawnSlot.LootDefinition = new LootSpawn
		{
			Items = new ItemAmountRanged[1]
			{
				new ItemAmountRanged
				{
					Shortname = shortname,
					MinAmount = 1f,
					MaxAmount = 1f
				}
			},
			SubSpawn = Array.Empty<LootSpawn.Entry>()
		};
		lootSpawnSlot.NumberToSpawn = 1;
		lootSpawnSlot.Probability = 0.01f;
		LootSpawnSlot item = lootSpawnSlot;
		List<LootSpawnSlot> list = new List<LootSpawnSlot>();
		if (profile.LootSpawnSlots != null)
		{
			list.AddRange(profile.LootSpawnSlots);
		}
		list.Add(item);
		profile.LootSpawnSlots = list.ToArray();
		return true;
	}

	private static bool SlotContainsItem(LootSpawnSlot slot, string shortname)
	{
		return CountInSpawn(slot?.LootDefinition, shortname) > 0;
	}

	private static int CountInSpawn(LootSpawn spawn, string shortname)
	{
		if (spawn == null)
		{
			return 0;
		}
		int num = 0;
		if (spawn.Items != null)
		{
			ItemAmountRanged[] items = spawn.Items;
			for (int i = 0; i < items.Length; i++)
			{
				if (items[i]?.Shortname == shortname)
				{
					num++;
				}
			}
		}
		if (spawn.SubSpawn != null)
		{
			LootSpawn.Entry[] subSpawn = spawn.SubSpawn;
			for (int i = 0; i < subSpawn.Length; i++)
			{
				num += CountInSpawn(subSpawn[i]?.Category, shortname);
			}
		}
		return num;
	}

	public static void SaveData(AlphaLootMod mod)
	{
		if (mod != null && mod.Config != null)
		{
			string basePath = GetBasePath(mod);
			if (!string.IsNullOrEmpty(basePath))
			{
				string path = Path.Combine(basePath, "LootProfiles");
				TrySave(mod.StoredData, Path.Combine(path, (mod.Config.ProfileName ?? "default_loottable") + ".json"));
				TrySave(mod.HeliData, Path.Combine(path, (mod.Config.HeliProfileName ?? "default_heli_loottable") + ".json"));
				TrySave(mod.BradleyData, Path.Combine(path, (mod.Config.BradleyProfileName ?? "default_bradley_loottable") + ".json"));
			}
		}
	}

	private static void TrySave(StoredData data, string path)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		try
		{
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			JsonSerializerSettings val = new JsonSerializerSettings
			{
				Formatting = (Formatting)1,
				ReferenceLoopHandling = (ReferenceLoopHandling)1
			};
			File.WriteAllText(path, JsonConvert.SerializeObject((object)(data ?? new StoredData()), val));
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[AlphaLoot] Save error " + path + ": " + ex.Message));
		}
	}

	private static string GetBasePath(AlphaLootMod mod)
	{
		return mod?.BaseDataPath;
	}

	private static void Reply(Arg arg, string msg)
	{
		if (((arg != null) ? arg.Connection : null) != null)
		{
			arg.ReplyWith(msg);
		}
		else
		{
			Debug.Log((object)("[AlphaLoot] " + msg));
		}
	}
}
