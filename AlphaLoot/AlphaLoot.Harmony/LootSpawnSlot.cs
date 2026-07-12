using System;
using Rust;

namespace AlphaLoot.Harmony;

public class LootSpawnSlot
{
	public LootSpawn LootDefinition;

	public int NumberToSpawn;

	public float Probability;

	public string OnlyWithLoadoutNamed;

	public Era[] Eras = Array.Empty<Era>();

	public LootSpawnSlot()
	{
	}

	public LootSpawnSlot(LootContainer.LootSpawnSlot gameSlot, bool hasCondition)
	{
		LootDefinition = new LootSpawn(gameSlot.definition, hasCondition);
		NumberToSpawn = gameSlot.numberToSpawn;
		Probability = gameSlot.probability;
		OnlyWithLoadoutNamed = gameSlot.onlyWithLoadoutNamed ?? "";
		Eras = gameSlot.eras ?? Array.Empty<Era>();
	}

	public LootSpawnSlot(global::LootSpawn gameSpawn, int numberToSpawn, bool hasCondition)
	{
		LootDefinition = new LootSpawn(gameSpawn, hasCondition);
		NumberToSpawn = numberToSpawn;
		Probability = 1f;
	}
}
