using System.Collections.Generic;
using Newtonsoft.Json;

namespace TruePVE;

public class PreventLootingOptions
{
	[JsonProperty(PropertyName = "Enabled")]
	public bool Enabled = true;

	[JsonProperty(PropertyName = "Admins Can Always Loot")]
	public bool AdminCanLoot = true;

	[JsonProperty(PropertyName = "Allow Looting Players")]
	public bool AllowLootingPlayers;

	[JsonProperty(PropertyName = "Allow Looting Corpses")]
	public bool AllowLootingCorpses;

	[JsonProperty(PropertyName = "Allow Looting Storage Containers")]
	public bool AllowLootingStorageContainers;

	[JsonProperty(PropertyName = "Use Teams For Allies")]
	public bool UseTeamsForAllies = true;

	[JsonProperty(PropertyName = "Use Friends API For Allies")]
	public bool UseFriendsAPIForAllies = true;

	[JsonProperty(PropertyName = "Respect Cupboard Authorization")]
	public bool RespectCupboardAuthorization = true;

	[JsonProperty(PropertyName = "Only In Cupboard Range (if true)")]
	public bool OnlyInCupboardRange;

	[JsonProperty(PropertyName = "Protect Planterboxes (Prevent unauthorized harvesting)")]
	public bool ProtectPlanterboxes = true;

	[JsonProperty(PropertyName = "Can Loot Backpack")]
	public bool CanLootBackpack;

	[JsonProperty(PropertyName = "Allow Looting Sleepers (non-ally can loot when true)")]
	public bool AllowLootingSleepers;

	[JsonProperty(PropertyName = "Debug Logging")]
	public bool Debug;

	[JsonProperty(PropertyName = "Debug")]
	private bool LegacyDebug
	{
		set
		{
			Debug = value;
		}
	}

	[JsonProperty(/*Could not decode attribute arguments.*/)]
	public List<string> ExcludedShortPrefabNames = new List<string>();

	[JsonProperty(/*Could not decode attribute arguments.*/)]
	public List<string> ExcludeEntities = new List<string> { "mailbox.deployed" };
}
