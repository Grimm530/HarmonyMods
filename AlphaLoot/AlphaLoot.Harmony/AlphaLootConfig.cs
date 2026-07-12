using System.Collections.Generic;
using Newtonsoft.Json;

namespace AlphaLoot.Harmony;

public class AlphaLootConfig
{
	[JsonProperty(PropertyName = "Loot Table Name")]
	public string ProfileName { get; set; } = "default_loottable";

	[JsonProperty(PropertyName = "Heli Loot Table Name")]
	public string HeliProfileName { get; set; } = "default_heli_loottable";

	[JsonProperty(PropertyName = "Bradley Loot Table Name")]
	public string BradleyProfileName { get; set; } = "default_bradley_loottable";

	[JsonProperty(PropertyName = "Amount of crates to drop (Bradley APC - default 3, Set to -1 to disable)")]
	public int BradleyCrates { get; set; } = -1;

	[JsonProperty(PropertyName = "Amount of crates to drop (Patrol Helicopter - default 4, Set to -1 to disable)")]
	public int HelicopterCrates { get; set; } = -1;

	[JsonProperty(PropertyName = "Global Loot Multiplier (multiplies all loot amounts by the number specified)")]
	public float GlobalMultiplier { get; set; } = 1f;

	[JsonProperty(PropertyName = "Apply global and individual loot multipliers to un-stackable items")]
	public bool MultiplyUnstackable { get; set; }

	[JsonProperty(PropertyName = "Override specified container profiles with another profile (profile name, override profile name)")]
	public Dictionary<string, string> ContainerOverrides { get; set; } = new Dictionary<string, string>();

	[JsonProperty(PropertyName = "Don't apply random workshop skins to the following items (shortnames)")]
	public HashSet<string> IgnoreSkinsFor { get; set; } = new HashSet<string>();

	[JsonProperty(PropertyName = "Use skins from the approved skin list (WARNING! Allowing users to use paid DLC they don't own is against Rusts TOS)")]
	public bool UseApprovedSkins { get; set; }

	[JsonProperty(PropertyName = "Force all loot items to spawn at full health/condition")]
	public bool ForceFullCondition { get; set; }

	[JsonProperty(PropertyName = "Override FancyDrop containers with supply drop profile")]
	public bool OverrideFancyDrop { get; set; }

	[JsonProperty(PropertyName = "Auto-update loot tables with new items")]
	public bool AutoUpdate { get; set; }

	[JsonProperty(PropertyName = "Debug: log supply drop loot to console when populated")]
	public bool DebugSupplyDrops { get; set; }

	[JsonProperty(PropertyName = "Debug: log loot table to console when populated")]
	public bool DebugLootTable { get; set; }

	public bool TryGetContainerOverride(string container, out string overrideProfile)
	{
		overrideProfile = null;
		if (ContainerOverrides != null)
		{
			return ContainerOverrides.TryGetValue(container, out overrideProfile);
		}
		return false;
	}
}
