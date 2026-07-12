using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class ItemAmount
{
	public class ConditionItem
	{
		public float MinCondition = 1f;

		public float MaxCondition = 1f;
	}

	public string Shortname;

	public float BlueprintChance;

	public float MinAmount;

	public string ItemName = "";

	public string ItemText = "";

	public ulong SkinID;

	public bool DontMultiply;

	public ConditionItem Condition = new ConditionItem();

	private int _itemId = -1;

	private ItemDefinition _itemDefinition;

	/// <summary>
	/// Collapses all whitespace for case-insensitive comparison (fixes "SewingKit" vs "Sewing Kit" style mismatches).
	/// </summary>
	internal static string CollapseComparableName(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (!char.IsWhiteSpace(c))
			{
				stringBuilder.Append(char.ToLowerInvariant(c));
			}
		}
		return stringBuilder.ToString();
	}

	/// <summary>
	/// When loot JSON stores a custom <see cref="ItemName"/> that matches the item's English display title except for
	/// whitespace / casing, use the canonical English display title so other systems see the same name as vanilla.
	/// </summary>
	internal static string ResolveCanonicalCustomItemName(ItemDefinition def, string requestedName)
	{
		if (def == null || string.IsNullOrEmpty(requestedName))
		{
			return requestedName;
		}
		string text = def.displayName?.english;
		if (string.IsNullOrEmpty(text))
		{
			return requestedName;
		}
		if (string.Equals(CollapseComparableName(text), CollapseComparableName(requestedName), StringComparison.Ordinal))
		{
			return text;
		}
		return requestedName;
	}

	private static ItemDefinition TryFindDefinitionByDisplayNameLabel(string label)
	{
		if (string.IsNullOrEmpty(label))
		{
			return null;
		}
		string b = CollapseComparableName(label);
		if (b.Length == 0)
		{
			return null;
		}
		List<ItemDefinition> itemList = ItemManager.itemList;
		if (itemList == null)
		{
			return null;
		}
		ItemDefinition itemDefinition = null;
		int num = 0;
		for (int i = 0; i < itemList.Count; i++)
		{
			ItemDefinition itemDefinition2 = itemList[i];
			if (itemDefinition2 == null)
			{
				continue;
			}
			string english = itemDefinition2.displayName?.english;
			if (string.IsNullOrEmpty(english))
			{
				continue;
			}
			if (string.Equals(CollapseComparableName(english), b, StringComparison.Ordinal))
			{
				num++;
				itemDefinition = itemDefinition2;
			}
		}
		if (num == 1)
		{
			return itemDefinition;
		}
		if (num > 1)
		{
			Debug.LogWarning((object)("[AlphaLoot.Harmony] Ambiguous item label '" + label + "' matched " + num + " definitions; use item shortname in loot JSON."));
		}
		return null;
	}

	[JsonIgnore]
	public int ItemID
	{
		get
		{
			if (_itemId >= 0)
			{
				return _itemId;
			}
			return _itemId = ((ItemDefinition != null) ? ItemDefinition.itemid : (-1));
		}
	}

	[JsonIgnore]
	public ItemDefinition ItemDefinition
	{
		get
		{
			if (_itemDefinition != null || string.IsNullOrEmpty(Shortname))
			{
				return _itemDefinition;
			}
			string text = Shortname.Trim();
			_itemDefinition = ItemManager.FindItemDefinition(text);
			if (_itemDefinition == null)
			{
				_itemDefinition = TryFindDefinitionByDisplayNameLabel(text);
			}
			return _itemDefinition;
		}
	}

	/// <summary>
	/// Game shortname when <see cref="ItemDefinition"/> resolved; otherwise the raw <see cref="Shortname"/> from JSON.
	/// </summary>
	[JsonIgnore]
	public string ResolvedShortname => ItemDefinition?.shortname ?? Shortname;

	public virtual float GetAmount(float lootMultiplier)
	{
		ItemDefinition itemDefinition = ItemDefinition;
		if (itemDefinition == null)
		{
			return 0f;
		}
		AlphaLootConfig config = AlphaLootContext.Config;
		if (((itemDefinition.stackable <= 1 || itemDefinition.condition.enabled) && (config == null || !config.MultiplyUnstackable)) || DontMultiply)
		{
			return Mathf.Clamp(MinAmount, 1f, float.MaxValue);
		}
		return Mathf.Clamp(MinAmount * lootMultiplier * (config?.GlobalMultiplier ?? 1f), 1f, float.MaxValue);
	}

	public ulong GetSkinID(bool allowRandomSkins)
	{
		ulong skinId = 0uL;
		if (SkinID != 0L)
		{
			skinId = SkinID;
		}
		else if (allowRandomSkins)
		{
			skinId = RandomSkinID();
		}
		return FilterBlockedSkin(skinId);
	}

	internal static ulong FilterBlockedSkin(ulong skinId)
	{
		if (skinId == 0L)
		{
			return 0uL;
		}
		AlphaLootConfig config = AlphaLootContext.Config;
		if (config != null && config.UseApprovedSkins)
		{
			return skinId;
		}
		if (AlphaLootContext.BlockedWorkshopSkinIds == null)
		{
			return skinId;
		}
		if (!AlphaLootContext.BlockedWorkshopSkinIds.Contains(skinId))
		{
			return skinId;
		}
		return 0uL;
	}

	private ulong RandomSkinID()
	{
		string keyName = ResolvedShortname;
		if (string.IsNullOrEmpty(keyName))
		{
			return 0uL;
		}
		if (AlphaLootContext.WeightedSkinIds != null && AlphaLootContext.WeightedSkinIds.TryGetValue(keyName, out var value) && value.Count > 0)
		{
			int num = 0;
			foreach (SkinEntry item in value)
			{
				num += item.Weight;
			}
			int num2 = Random.Range(0, num);
			foreach (SkinEntry item2 in value)
			{
				num -= item2.Weight;
				if (num2 >= num)
				{
					return item2.SkinID;
				}
			}
		}
		AlphaLootConfig config = AlphaLootContext.Config;
		if (config == null || config.IgnoreSkinsFor?.Contains(keyName) != true)
		{
			Dictionary<string, List<ulong>> importedSkinIds = AlphaLootContext.ImportedSkinIds;
			if (importedSkinIds != null && importedSkinIds.TryGetValue(keyName, out var value2) && value2.Count > 0)
			{
				return value2[Random.Range(0, value2.Count)];
			}
		}
		return 0uL;
	}

	public float GetConditionFraction()
	{
		AlphaLootConfig config = AlphaLootContext.Config;
		if ((config == null || !config.ForceFullCondition) && Condition != null)
		{
			return Random.Range(Condition.MinCondition, Condition.MaxCondition);
		}
		return 1f;
	}

	public bool WantsBlueprint()
	{
		return Random.Range(0f, 1f) < BlueprintChance;
	}
}
