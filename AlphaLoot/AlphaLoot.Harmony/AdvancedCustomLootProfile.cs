using UnityEngine;

namespace AlphaLoot.Harmony;

public class AdvancedCustomLootProfile : BaseLootProfile
{
	public LootSpawnSlot[] LootSpawnSlots;

	public int MaximumItems = 24;

	public override void PopulateLoot(ItemContainer container)
	{
		if (LootSpawnSlots != null && LootSpawnSlots.Length != 0)
		{
			BaseLootProfile.SetContainerCapacity(container, Mathf.Min(MaximumItems, 24));
			LootSpawnSlot[] lootSpawnSlots = LootSpawnSlots;
			foreach (LootSpawnSlot lootSpawnSlot in lootSpawnSlots)
			{
				if (lootSpawnSlot?.LootDefinition == null)
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
