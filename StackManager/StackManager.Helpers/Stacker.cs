using System.Collections.Generic;
using StackManager.Utility;
using UnityEngine;

namespace StackManager.Helpers;

public class Stacker
{
	private static bool Initialized;
	private static Dictionary<string, int> OriginalStackables;

	internal static void Initialize()
	{
		if (Initialized || ItemManager.itemList == null)
		{
			return;
		}
		Settings.LoadConfig();
		Initialized = true;
		OriginalStackables = new Dictionary<string, int>();

		// ItemExact: set exact stack sizes (second precedence; Blacklist takes priority)
		if (Settings.Config.ItemExact != null)
		{
			foreach (ItemDefinition item in ItemManager.itemList)
			{
				if (Settings.Config.Blacklist.Contains(item.shortname))
					continue;
				if (!Settings.Config.ItemExact.TryGetValue(item.shortname, out int exactStack))
					continue;
				OriginalStackables[item.shortname] = item.stackable;
				item.stackable = exactStack;
			}
		}

		foreach (KeyValuePair<ItemCategory, float> theCategory in Settings.Config.Category)
		{
			foreach (ItemDefinition item in ItemManager.itemList)
			{
				if (item.category != theCategory.Key || Settings.Config.Blacklist.Contains(item.shortname) || Settings.Config.Item.ContainsKey(item.shortname) || (Settings.Config.ItemExact?.ContainsKey(item.shortname) ?? false))
					continue;
				_ = item.stackable;
				item.stackable = Mathf.CeilToInt((float)item.stackable * theCategory.Value);
			}
		}
		foreach (ItemDefinition item2 in ItemManager.itemList)
		{
			if (!Settings.Config.Item.ContainsKey(item2.shortname) || (Settings.Config.ItemExact?.ContainsKey(item2.shortname) ?? false))
				continue;
			_ = item2.stackable;
			item2.stackable = Mathf.CeilToInt((float)item2.stackable * Settings.Config.Item[item2.shortname]);
		}
		Log.Information("Item stacks patched");
	}

	internal static void Kill()
	{
		if (!Initialized)
		{
			return;
		}
		Log.Information("Rolling back item manager");

		// Restore ItemExact items from stored originals
		if (OriginalStackables != null)
		{
			foreach (ItemDefinition item in ItemManager.itemList)
			{
				if (OriginalStackables.TryGetValue(item.shortname, out int original))
					item.stackable = original;
			}
			OriginalStackables = null;
		}

		foreach (KeyValuePair<ItemCategory, float> theCategory in Settings.Config.Category)
		{
			foreach (ItemDefinition item in ItemManager.itemList)
			{
				if (item.category != theCategory.Key || Settings.Config.Blacklist.Contains(item.shortname) || Settings.Config.Item.ContainsKey(item.shortname) || (Settings.Config.ItemExact?.ContainsKey(item.shortname) ?? false))
					continue;
				item.stackable = Mathf.CeilToInt((float)item.stackable / theCategory.Value);
			}
		}
		foreach (ItemDefinition item2 in ItemManager.itemList)
		{
			if (!Settings.Config.Item.ContainsKey(item2.shortname) || (Settings.Config.ItemExact?.ContainsKey(item2.shortname) ?? false))
				continue;
			item2.stackable = Mathf.CeilToInt((float)item2.stackable / Settings.Config.Item[item2.shortname]);
		}
		Initialized = false;
		Settings.ClearCache();
	}
}
