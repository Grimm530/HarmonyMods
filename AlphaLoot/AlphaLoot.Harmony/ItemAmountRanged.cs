using System;
using UnityEngine;

namespace AlphaLoot.Harmony;

public class ItemAmountRanged : ItemAmount
{
	public float MaxAmount = -1f;

	public ItemAmountRanged()
	{
	}

	public ItemAmountRanged(ItemDefinition itemDef, float amount, float maxAmount, bool hasCondition)
	{
		Shortname = itemDef?.shortname ?? "";
		BlueprintChance = (((itemDef != null) && itemDef.spawnAsBlueprint) ? 1f : 0f);
		MinAmount = amount;
		MaxAmount = Mathf.Max(maxAmount, amount);
		if (hasCondition && itemDef != null && itemDef.condition.enabled)
		{
			Condition = new ConditionItem
			{
				MinCondition = itemDef.condition.foundCondition.fractionMin,
				MaxCondition = itemDef.condition.foundCondition.fractionMax
			};
		}
	}

	public void Create(ItemContainer container, float lootMultiplier, bool allowSkinnedItems, bool expandContainer, ref int itemCount)
	{
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Unknown result type (might be due to invalid IL or missing references)
		if (DontMultiply && container != null)
		{
			for (int i = 0; i < container.itemList.Count; i++)
			{
				if (container.itemList[i]?.info != null && string.Equals(container.itemList[i].info.shortname, ResolvedShortname, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}
		}
		Item item = null;
		if (WantsBlueprint())
		{
			ItemDefinition blueprintBaseDefinition = BaseLootProfile.BlueprintBaseDefinition;
			if (blueprintBaseDefinition == null)
			{
				return;
			}
			item = ItemManager.Create(blueprintBaseDefinition, 1, 0uL, true, 0uL);
			item.blueprintTarget = base.ItemID;
		}
		else
		{
			item = ItemManager.CreateByItemID(base.ItemID, (int)GetAmount(lootMultiplier), GetSkinID(allowSkinnedItems));
			if (!string.IsNullOrEmpty(ItemName))
			{
				item.name = ItemAmount.ResolveCanonicalCustomItemName(item.info, ItemName);
			}
			if (!string.IsNullOrEmpty(ItemText))
			{
				item.text = ItemText;
			}
			if (item.hasCondition)
			{
				item.condition = GetConditionFraction() * item.info.condition.max;
			}
		}
		item.OnVirginSpawn();
		if (!item.MoveToContainer(container))
		{
			if (container.playerOwner == null)
			{
				item.Remove();
			}
			else
			{
				item.Drop(container.playerOwner.GetDropPosition(), container.playerOwner.GetDropVelocity(), Quaternion.identity);
			}
		}
		itemCount++;
		CreateAdditionalItems(container, lootMultiplier, allowSkinnedItems, expandContainer, ref itemCount);
	}

	public virtual void CreateAdditionalItems(ItemContainer container, float lootMultiplier, bool allowSkinnedItems, bool expandContainer, ref int itemCount)
	{
	}

	public override float GetAmount(float lootMultiplier)
	{
		ItemDefinition itemDefinition = base.ItemDefinition;
		if (itemDefinition == null)
		{
			return 0f;
		}
		AlphaLootConfig config = AlphaLootContext.Config;
		bool flag = (itemDefinition.stackable > 1 && !itemDefinition.condition.enabled) || (config?.MultiplyUnstackable ?? false);
		if (MinAmount == MaxAmount)
		{
			if (!flag || DontMultiply)
			{
				return Mathf.Clamp(MinAmount, 1f, float.MaxValue);
			}
			return Mathf.Clamp(MinAmount * lootMultiplier * (config?.GlobalMultiplier ?? 1f), 1f, float.MaxValue);
		}
		if (!flag || DontMultiply)
		{
			return Mathf.Clamp(Random.Range(MinAmount, MaxAmount), 1f, float.MaxValue);
		}
		return Mathf.Clamp(Random.Range(MinAmount, MaxAmount) * lootMultiplier * (config?.GlobalMultiplier ?? 1f), 1f, float.MaxValue);
	}
}
