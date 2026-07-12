using System.Collections.Generic;

namespace StackManager.Config;

public class DefaultConfig
{
	public HashSet<string> Blacklist = new HashSet<string>
	{
		"water",
		"water.salt",
		"blueprintbase",
		"flare",
		"generator.wind.scrap",
		"battery.small",
		"building.planner",
		"door.key",
		"map",
		"note",
		"hat.candle",
		"hat.miner",
		"skull.trophy",
		"skull.trophy.table",
		"skull.trophy.jar2",
		"skull.trophy.jar",
		"head.bag"
	};

	public Dictionary<ItemCategory, float> Category = new Dictionary<ItemCategory, float>
	{
		{ ItemCategory.Ammunition, 1f },
		{ ItemCategory.Attire, 1f },
		{ ItemCategory.Component, 1f },
		{ ItemCategory.Construction, 1f },
		{ ItemCategory.Electrical, 1f },
		{ ItemCategory.Food, 1f },
		{ ItemCategory.Fun, 1f },
		{ ItemCategory.Items, 1f },
		{ ItemCategory.Medical, 1f },
		{ ItemCategory.Misc, 1f },
		{ ItemCategory.Resources, 1f },
		{ ItemCategory.Tool, 1f },
		{ ItemCategory.Traps, 1f },
		{ ItemCategory.Weapon, 1f }
	};

	public Dictionary<string, float> Item = new Dictionary<string, float> { { "explosive.timed", 1f } };

	/// <summary>Per-item exact stack sizes. Overrides Category and Item multipliers. Use for specific stack amounts.</summary>
	public Dictionary<string, int> ItemExact = new Dictionary<string, int>
	{
		{ "syringe.medical", 25 },
		{ "largemedkit", 10 },
		{ "antiradpills", 100 },
		{ "bandage", 100 },
		{ "blood", 100000 },
		{ "paper", 100000 }
	};
}
