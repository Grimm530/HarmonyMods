using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterAirDrop;

public class HarmonyConfig
{
	public class ConfigData
	{
		public float PlaneSpeedMultiplier = 1f;

		public float PlaneAdditionalHeight = 250f;

		/// <summary>Rigidbody drag for supply crates. Lower = faster fall (1f ≈ 3x faster than default 3f).</summary>
		public float CrateAirResistance = 1f;

		public bool ExactAirDrop = false;

		public bool RemoveSupplySignalSmoke = false;

		/// <summary>When true, supply signals spawn the crate instantly in front of the player instead of calling a cargo plane.</summary>
		public bool InstantSupplyDrops = true;

		/// <summary>When true (and InstantSupplyDrops is true), the crate spawns in the sky at the drop location and falls using CrateAirResistance. No plane is used.</summary>
		public bool InstantSupplyDropFallsFromSky = false;
	}

	public static ConfigData Config;

	public static bool Loaded = false;

	private static readonly string Location = Path.Combine("HarmonyConfig", "BetterAirDrop.json");

	public static void LoadConfig()
	{
		if (Loaded)
		{
			return;
		}
		if (!Directory.Exists("HarmonyConfig"))
		{
			Directory.CreateDirectory("HarmonyConfig");
		}
		if (!File.Exists(Location))
		{
			LoadDefaultConfig();
		}
		else
		{
			try
			{
				Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location));
				MergeDefaultsAndSave();
			}
			catch
			{
				LoadDefaultConfig();
			}
		}
		Loaded = true;
	}

	/// <summary>Adds any missing config keys from defaults and writes the file back.</summary>
	private static void MergeDefaultsAndSave()
	{
		ConfigData defaults = new ConfigData();
		JObject existing = JObject.FromObject(Config);
		JObject defaultKeys = JObject.FromObject(defaults);
		bool updated = false;
		foreach (var prop in defaultKeys.Properties())
		{
			if (existing[prop.Name] == null)
			{
				existing[prop.Name] = prop.Value;
				updated = true;
			}
		}
		if (updated)
		{
			File.WriteAllText(Location, existing.ToString(Formatting.Indented));
		}
	}

	private static void LoadDefaultConfig()
	{
		Config = new ConfigData();
		File.WriteAllText(Location, JToken.Parse(JsonConvert.SerializeObject((object)Config)).ToString((Formatting)1, Array.Empty<JsonConverter>()));
	}
}
