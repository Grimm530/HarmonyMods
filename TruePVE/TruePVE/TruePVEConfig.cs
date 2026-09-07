using Newtonsoft.Json;

namespace TruePVE;

public class TruePVEConfig
{
	[JsonProperty(PropertyName = "Prevent Looting")]
	public PreventLootingOptions PreventLooting = new PreventLootingOptions();

	[JsonProperty(PropertyName = "Loot Defender")]
	public LootDefenderOptions LootDefender = new LootDefenderOptions();

	[JsonProperty(PropertyName = "PvE")]
	public PvEOptions PvE = new PvEOptions();
}
