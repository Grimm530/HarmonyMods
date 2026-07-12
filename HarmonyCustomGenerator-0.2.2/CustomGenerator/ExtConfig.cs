using System;
using System.Collections.Generic;
using System.IO;
using CustomGenerator.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CustomGenerator;

public class ExtConfig
{
	public class ConfigData
	{
		[JsonProperty("Map Settings")]
		public MapSettings mapSettings = new MapSettings();

		[JsonProperty("Main Generator")]
		public GeneratorSettings Generator = new GeneratorSettings();

		[JsonProperty("Swap Monuments")]
		public SwapSettings Swap = new SwapSettings();

		[JsonProperty("Monuments")]
		public MonumentSettings Monuments = new MonumentSettings();

		public string Version = CurrentVersion;
	}

	public sealed class MapSettings
	{
		[JsonProperty("Generate new map everytime")]
		public bool GenerateNewMapEverytime = true;

		[JsonProperty("Override Map Sizes (9000 not be changed to 6000)")]
		public bool OverrideSizes = true;

		[JsonProperty("Override Map Folder (saves to <Server Root>/maps/)")]
		public bool OverrideFolder = true;

		[JsonProperty("Override Map Name")]
		public bool OverrideName = true;

		[JsonProperty("Map Name ({0} - size, {1} - seed)")]
		public string MapName = "CustomGenerator{0}_{1}";
	}

	public sealed class GeneratorSettings
	{
		public SimplePath Road = new SimplePath();

		public SimplePath Rail = new SimplePath();

		public UniqueEnviroment UniqueEnviroment = new UniqueEnviroment();

		[JsonProperty("Remove Rivers")]
		public bool RemoveRivers;

		[JsonProperty("Remove Car Wrecks around Road")]
		public bool RemoveCarWrecks;

		[JsonProperty("Allow building on road")]
		public bool AllowRoadBuild;

		[JsonProperty("Remove tunnel entrances")]
		public bool RemoveTunnelsEntrances;

		[JsonProperty("Change percentages")]
		public bool ModifyPercentages;

		[JsonProperty("Tier Percentages (100 in total)")]
		public TierSettings Tier = new TierSettings();

		[JsonProperty("Bioms Percentages (100 in total) - idk why jungle 70%")]
		public BiomSettings Biom = new BiomSettings();
	}

	public sealed class SwapSettings
	{
		[JsonProperty("Enabled")]
		public bool Enabled;

		[JsonProperty("Save both maps (with swap and without)")]
		public bool SaveBothMaps;
	}

	public class MonumentSettings
	{
		[JsonProperty("Enabled")]
		public bool Enabled;

		[JsonProperty("MonumentList")]
		public List<Monument> monuments = new List<Monument>();
	}

	public class Monument
	{
		public bool ShouldChange;

		public bool Generate;

		public string Description;

		public string Folder;

		public int MinWorldSize;

		public int TargetCount;

		[JsonConverter(typeof(StringEnumConverter))]
		public PlaceMonuments.DistanceMode distanceSame = PlaceMonuments.DistanceMode.Max;

		public int MinDistanceSameType = 500;

		[JsonConverter(typeof(StringEnumConverter))]
		public PlaceMonuments.DistanceMode distanceDifferent;

		public int MinDistanceDifferentType;

		public SpawnFilterCfg Filter = new SpawnFilterCfg();
	}

	public class SpawnFilterCfg
	{
		public bool Enabled;

		public List<string> SplatType = new List<string>();

		public List<string> BiomeType = new List<string>();

		public List<string> TopologyAny = new List<string>();

		public List<string> TopologyAll = new List<string>();

		public List<string> TopologyNot = new List<string>();
	}

	public class SimplePath
	{
		public bool ShouldChange = true;

		public bool Enabled = true;

		public bool GenerateRing = true;

		public bool GenerateSideMonuments = true;

		public bool GenerateSideObjects;
	}

	public class UniqueEnviroment
	{
		public bool ShouldChange = true;

		public bool GenerateOasis = true;

		public bool GenerateCanyons = true;

		public bool GenerateLakes = true;
	}

	public sealed class TierSettings
	{
		public float Tier0 = 30f;

		public float Tier1 = 30f;

		public float Tier2 = 40f;

		[NonSerialized]
		public readonly float DefaultTier0 = 40f;

		[NonSerialized]
		public readonly float DefaultTier1 = 15f;

		[NonSerialized]
		public readonly float DefaultTier2 = 15f;
	}

	public sealed class BiomSettings
	{
		public float Arid = 40f;

		public float Temperate = 15f;

		public float Tundra = 15f;

		public float Arctic = 30f;

		public float Jungle = 70f;

		[NonSerialized]
		public readonly float DefaultArid = 40f;

		[NonSerialized]
		public readonly float DefaultTemperate = 15f;

		[NonSerialized]
		public readonly float DefaultTundra = 15f;

		[NonSerialized]
		public readonly float DefaultArctic = 30f;

		[NonSerialized]
		public readonly float DefaultJungle = 70f;
	}

	public sealed class TempData
	{
		public uint mapsize;

		public uint mapseed;

		public bool mapGenerated;

		public bool shouldGetMonuments;

		public TerrainTexturing terrainTexturing;

		public TerrainMeta terrainMeta;

		public TerrainPath terrainPath;
	}

	public const bool EN = true;

	public static ConfigData Config;

	public static TempData tempData;

	private static readonly string CurrentVersion;

	private static readonly string Location;

	static ExtConfig()
	{
		CurrentVersion = "0.2.2";
		Location = Path.Combine("HarmonyConfig", "CustomGenerator.json");
		LoadConfig();
	}

	private static void LoadConfig()
	{
		tempData = new TempData();
		if (!Directory.Exists("HarmonyConfig"))
		{
			Directory.CreateDirectory("HarmonyConfig");
			Logging.Info("Created HarmonyConfig directory");
		}
		if (!File.Exists(Location))
		{
			Logging.Info("Config file not found, creating default configuration");
			LoadDefaultConfig();
			return;
		}
		try
		{
			ConfigData configData = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location));
			if (configData.Version != CurrentVersion)
			{
				Logging.Config("Version mismatch! Old: " + configData.Version + ", Current: " + CurrentVersion);
				Logging.Config("Creating backup and migrating settings...");
				string text = Location + "." + configData.Version + ".backup";
				File.WriteAllText(text, JsonConvert.SerializeObject((object)configData, (Formatting)1));
				Logging.Config("Backup created at: " + text);
				Config = new ConfigData();
				if (configData.mapSettings != null)
				{
					Config.mapSettings.GenerateNewMapEverytime = configData.mapSettings.GenerateNewMapEverytime;
					Config.mapSettings.OverrideSizes = configData.mapSettings.OverrideSizes;
					Config.mapSettings.OverrideFolder = configData.mapSettings.OverrideFolder;
					Config.mapSettings.OverrideName = configData.mapSettings.OverrideName;
					Config.mapSettings.MapName = configData.mapSettings.MapName;
					Logging.Config("Map settings migrated");
				}
				if (configData.Generator != null)
				{
					Config.Generator.Road = configData.Generator.Road;
					Config.Generator.Rail = configData.Generator.Rail;
					Config.Generator.UniqueEnviroment = configData.Generator.UniqueEnviroment;
					Config.Generator.RemoveCarWrecks = configData.Generator.RemoveCarWrecks;
					Config.Generator.RemoveRivers = configData.Generator.RemoveRivers;
					Config.Generator.RemoveTunnelsEntrances = configData.Generator.RemoveTunnelsEntrances;
					Config.Generator.ModifyPercentages = configData.Generator.ModifyPercentages;
					Config.Generator.Tier = configData.Generator.Tier;
					Config.Generator.Biom = configData.Generator.Biom;
					Logging.Config("Generator settings migrated");
				}
				if (configData.Swap != null)
				{
					Config.Swap.Enabled = configData.Swap.Enabled;
					Config.Swap.SaveBothMaps = configData.Swap.SaveBothMaps;
					Logging.Config("Swap settings migrated");
				}
				if (configData.Monuments != null)
				{
					Config.Monuments.Enabled = configData.Monuments.Enabled;
					Config.Monuments.monuments = configData.Monuments.monuments;
					Logging.Config("Monument settings migrated");
				}
				SaveConfig();
				Logging.Config("Settings migration completed successfully");
			}
			else
			{
				Config = configData;
				Logging.Config("Configuration loaded successfully");
			}
			if (Config.Monuments.monuments.IsNullOrEmpty())
			{
				tempData.shouldGetMonuments = true;
			}
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to load configuration", ex);
			Logging.Config("Loading default configuration...");
			LoadDefaultConfig();
		}
	}

	private static void LoadDefaultConfig()
	{
		try
		{
			Config = new ConfigData();
			SaveConfig();
			Logging.Config("Default configuration created successfully");
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to create default configuration", ex);
		}
	}

	public static void SaveConfig()
	{
		try
		{
			File.WriteAllText(Location, JsonConvert.SerializeObject((object)Config, (Formatting)1));
			Logging.Config("Configuration saved successfully");
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to save configuration", ex);
		}
	}
}
