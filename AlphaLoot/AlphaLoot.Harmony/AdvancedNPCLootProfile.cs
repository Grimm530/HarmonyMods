using UnityEngine;

namespace AlphaLoot.Harmony;

public class AdvancedNPCLootProfile : BaseLootProfile
{
	public LootSpawnSlot[] LootSpawnSlots;

	public int MaximumItems = -1;

	public AdvancedNPCLootProfile()
	{
	}

	public AdvancedNPCLootProfile(LootContainer.LootSpawnSlot[] lootSpawnSlots)
	{
		LootSpawnSlots = new LootSpawnSlot[(lootSpawnSlots != null) ? lootSpawnSlots.Length : 0];
		for (int i = 0; i < ((lootSpawnSlots != null) ? lootSpawnSlots.Length : 0); i++)
		{
			LootSpawnSlots[i] = new LootSpawnSlot(lootSpawnSlots[i], hasCondition: true);
		}
	}

	public override void PopulateLoot(ItemContainer container, string loadoutName)
	{
		if (LootSpawnSlots != null && LootSpawnSlots.Length != 0)
		{
			BaseLootProfile.SetContainerCapacity(container, (MaximumItems == -1) ? 36 : MaximumItems);
			LootSpawnSlot[] lootSpawnSlots = LootSpawnSlots;
			foreach (LootSpawnSlot lootSpawnSlot in lootSpawnSlots)
			{
				if (lootSpawnSlot?.LootDefinition == null || (!string.IsNullOrEmpty(lootSpawnSlot.OnlyWithLoadoutNamed) && lootSpawnSlot.OnlyWithLoadoutNamed != loadoutName))
				{
					continue;
				}
				for (int j = 0; j < lootSpawnSlot.NumberToSpawn; j++)
				{
					if (Random.Range(0f, 1f) <= lootSpawnSlot.Probability)
					{
						lootSpawnSlot.LootDefinition.SpawnIntoContainer(container, this);
					}
				}
			}
		}
		base.PopulateLoot(container);
	}
}
