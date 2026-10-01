using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CustomGenerator.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace CustomGenerator;

public class ExtConfig
{
	public class ConfigData
	{
		[JsonProperty("$schema", Order = -3)]
		[Schema(ReadOnly = true)]
		public string SchemaPath = "./" + Path.GetFileName(SchemaLocation);

		[JsonProperty("Language (en/ru)", Order = -2)]
		[Desc("Config language. Change it and restart the server: keys are rewritten in this language, values are kept", "Язык конфига. Измените и перезапустите сервер: ключи перепишутся на этом языке, значения сохранятся")]
		[Schema(Values = new string[] { "en", "ru" })]
		public string Language = DetectLanguage();

		[Loc("Map Settings", "Настройки Карты")]
		public MapSettings mapSettings = new MapSettings();

		[Loc("Map Image", "Превью Карты")]
		public MapImageSettings MapImage = new MapImageSettings();

		[Loc("Main Generator", "Основной Генератор")]
		public GeneratorSettings Generator = new GeneratorSettings();

		[Loc("Swap Monuments", "Замена Монументов")]
		public SwapSettings Swap = new SwapSettings();

		[Loc("Monuments", "Монументы")]
		public MonumentSettings Monuments = new MonumentSettings();

		[Loc("Custom Monuments", "Кастомные Монументы")]
		public CustomMonumentSettings CustomMonuments = new CustomMonumentSettings();

		[JsonProperty(Order = -1)]
		[Desc("Mod version the config was written by. Don't edit", "Версия мода, которой записан конфиг. Не изменяйте")]
		[Schema(ReadOnly = true)]
		public string Version = CurrentVersion;
	}

	public sealed class MapSettings
	{
		[Loc("Generate new map everytime", "Генерировать новую карту каждый раз")]
		public bool GenerateNewMapEverytime = true;

		[Loc("Override Map Sizes (9000 not be changed to 6000)", "Принудительный размер карты (карта 9000 не сменится на 6000)")]
		public bool OverrideSizes = true;

		[Loc("Override Map Folder (saves to <Server Root>/maps/)", "Перезаписать папку с картой (<папка сервера>/maps/)")]
		[Desc("Save maps to maps/ next to the mod's files. With the launcher they always go to the launcher's maps/", "Сохранять карты в maps/ рядом с файлами мода. С лаунчером они всегда идут в maps/ папки лаунчера")]
		public bool OverrideFolder = true;

		[Loc("Override Map Name", "Перезаписать название карты")]
		public bool OverrideName = true;

		[Loc("Map Name ({0} - size, {1} - seed)", "Название карты ({0} - размер, {1} - сид)")]
		public string MapName = "CustomGenerator{0}_{1}";
	}

	public sealed class MapImageSettings
	{
		[Loc("Enabled", "Включить")]
		public bool Enabled = true;

		[Loc("Scale (pixels per meter)", "Масштаб (пикселей на метр)")]
		[Desc("0.75 on a 4000 map gives a 3000 px map plus the ocean margin", "0.75 на карте 4000 даёт 3000 px карты плюс отступ океана")]
		[Schema(Min = 0.1, Max = 4.0)]
		public float Scale = 0.75f;

		[Loc("Ocean margin (pixels)", "Отступ океана (пиксели)")]
		[Desc("Ocean around the map on each side", "Океан вокруг карты с каждой стороны")]
		[Schema(Min = 0.0)]
		public int OceanMargin = 350;

		[Loc("Draw grid", "Рисовать сетку")]
		public bool Grid = true;

		[Loc("Draw monument names", "Подписывать монументы")]
		public bool MonumentNames = true;
	}

	public sealed class GeneratorSettings
	{
		[Desc("Roads: ring road and roadside monuments/objects", "Дороги: кольцевая дорога и придорожные монументы/объекты")]
		public SimplePath Road = new SimplePath();

		[Desc("Rails: ring rail and railside monuments", "Железная дорога: кольцо и монументы у железной дороги")]
		public SimplePath Rail = new SimplePath();

		[Desc("Oases, canyons and lakes on any map size", "Оазисы, каньоны и озёра на любом размере карты")]
		public UniqueEnviroment UniqueEnviroment = new UniqueEnviroment();

		[Loc("Remove Rivers", "Удалить реки")]
		public bool RemoveRivers;

		[Loc("River width scale (1 = default)", "Множитель ширины рек (1 = по умолчанию)")]
		[Schema(Min = 0.0)]
		public float RiverWidthScale = 1f;

		[Loc("Remove Car Wrecks around Road", "Удалить разбитые префабы машин около дороги")]
		public bool RemoveCarWrecks;

		[Loc("Allow building on road", "Разрешить строительство на дорогах")]
		public bool AllowRoadBuild;

		[Loc("Remove large powerlines", "Удалить большие ЛЭП")]
		public bool RemovePowerlines;

		[Loc("Remove tunnel entrances", "Удалить входы в туннели")]
		public bool RemoveTunnelsEntrances;

		[Loc("Remove underground tunnels (also removes entrances)", "Удалить подземные туннели (вместе со входами)")]
		public bool RemoveTunnels;

		[Loc("Change percentages", "Изменить проценты")]
		public bool ModifyPercentages;

		[Loc("Tier Percentages (100 in total)", "Проценты Тиров (всего 100)")]
		[Schema(SumTo = 100.0)]
		public TierSettings Tier = new TierSettings();

		[Loc("Biome Percentages (Arid+Temperate+Tundra+Arctic = 100, Jungle is separate)", "Проценты Биомов (Пустыня+Умеренный+Тундра+Арктика = 100, Джунгли отдельно)")]
		[Schema(SumTo = 100.0, SumFields = "Arid,Temperate,Tundra,Arctic")]
		public BiomSettings Biom = new BiomSettings();
	}

	public sealed class CustomMonumentSettings
	{
		[Loc("Enabled", "Включить")]
		public bool Enabled;

		[Loc("Folder with .map files (relative to server root)", "Папка с .map файлами (относительно папки сервера)")]
		public string Folder = "maps/custom";

		[Loc("List", "Список")]
		[Schema(ItemTitle = "Name,File", Addable = true)]
		public List<CustomMonument> List = new List<CustomMonument>();
	}

	public class CustomMonument
	{
		[Desc("Place this monument", "Размещать этот монумент")]
		public bool Enabled = true;

		[Desc("Name for the log, the file name is used if empty", "Имя для лога, если пусто - используется имя файла")]
		public string Name = "";

		[Desc("File in the custom monuments folder, e.g. \"my_gas_station.map\" or \"my_gas_station.prefab\"", "Файл в папке кастомных монументов, например \"my_gas_station.map\" или \"my_gas_station.prefab\"")]
		[Schema(Suggest = "customFiles")]
		public string File = "";

		[Desc("How many copies to place (fewer if there is no room)", "Сколько копий разместить (меньше, если не хватит места)")]
		[Schema(Min = 0.0)]
		public int Count = 1;

		[Desc("Footprint radius in meters, 0 = auto from prefab positions", "Радиус площадки в метрах, 0 = автоматически по префабам")]
		[Schema(Min = 0.0)]
		public float Radius;

		[Desc("Width of the transition ring between the monument terrain and the world, meters", "Ширина переходного кольца между рельефом монумента и миром, в метрах")]
		[Schema(Min = 0.0)]
		public float Blend = 25f;

		[Desc("Stamp = terrain heights from the file, Flatten = flat pad, None = keep world terrain", "Stamp = рельеф из файла, Flatten = ровная площадка, None = оставить рельеф мира")]
		[Schema(Values = new string[] { "Stamp", "Flatten", "None" })]
		public string HeightMode = "Stamp";

		[Desc("Copy ground textures from the file", "Копировать текстуры земли из файла")]
		public bool CopySplat;

		[Desc("Copy topology from the file", "Копировать топологию из файла")]
		public bool CopyTopology;

		[Desc("Copy terrain holes (e.g. bunker entrances)", "Копировать дыры в рельефе (например, входы в бункеры)")]
		public bool CopyAlpha = true;

		[Desc("Rotate every copy randomly", "Поворачивать каждую копию случайно")]
		public bool RandomRotation = true;

		[Desc("Max height difference of the world terrain under the footprint, meters", "Макс. перепад высот рельефа мира под площадкой, в метрах")]
		[Schema(Min = 0.0)]
		public float MaxHeightDifference = 15f;

		[Desc("Min terrain height (above sea level) at the center", "Мин. высота рельефа (над уровнем моря) в центре")]
		public float MinHeight = 2f;

		[Desc("Max terrain height (above sea level) at the center", "Макс. высота рельефа (над уровнем моря) в центре")]
		public float MaxHeight = 150f;

		[Desc("Min distance to any other monument, meters", "Мин. расстояние до любого другого монумента, в метрах")]
		[Schema(Min = 0.0)]
		public int MinDistanceToMonuments = 150;

		[Desc("Min distance between copies of this monument, meters", "Мин. расстояние между копиями этого монумента, в метрах")]
		[Schema(Min = 0.0)]
		public int MinDistanceSameType = 500;

		[Desc("Where the monument may stand", "Где может стоять монумент")]
		public SpawnFilterCfg Filter = new SpawnFilterCfg();
	}

	public sealed class SwapSettings
	{
		[Loc("Enabled", "Включить")]
		public bool Enabled;

		[Loc("Save both maps (with swap and without)", "Сохранить обе карты (с заменой и без)")]
		public bool SaveBothMaps;
	}

	public class MonumentSettings
	{
		[Loc("Enabled", "Включить")]
		public bool Enabled;

		[Loc("MonumentList", "Лист монументов")]
		[Schema(ItemTitle = "Description,Folder")]
		public List<Monument> monuments = new List<Monument>();
	}

	public class Monument
	{
		[Desc("true = apply the settings below, false = keep the group vanilla", "true = применить настройки ниже, false = оставить группу стандартной")]
		public bool ShouldChange;

		[Desc("false = don't generate the group at all (needs ShouldChange: true)", "false = не генерировать группу вообще (нужен ShouldChange: true)")]
		public bool Generate;

		[Desc("Group name (for the log)", "Название группы (для лога)")]
		public string Description;

		[Desc("Group path in the bundles, the group is found by it. Don't change, use OverrideFolder", "Путь группы в бандлах, по нему ищется группа. Не изменяйте, используйте OverrideFolder")]
		[Schema(ReadOnly = true)]
		public string Folder;

		[Desc("Min map size for the group to appear, 0 = any", "Мин. размер карты, на котором появляется группа, 0 = любой")]
		[Schema(Min = 0.0)]
		public int MinWorldSize;

		[Desc("How many monuments to place, 0 = every prefab of the group", "Сколько монументов разместить, 0 = все префабы группы")]
		[Schema(Min = 0.0)]
		public int TargetCount;

		[JsonConverter(typeof(StringEnumConverter))]
		[Desc("Placement preference relative to the same group: Max = far, Min = close, Any = no preference", "Предпочтение размещения относительно своей группы: Max = подальше, Min = поближе, Any = без разницы")]
		public PlaceMonuments.DistanceMode distanceSame = PlaceMonuments.DistanceMode.Max;

		[Desc("Min distance to monuments of the same group, meters", "Мин. расстояние до монументов своей группы, в метрах")]
		[Schema(Min = 0.0)]
		public int MinDistanceSameType = 500;

		[JsonConverter(typeof(StringEnumConverter))]
		[Desc("Placement preference relative to other groups: Max = far, Min = close, Any = no preference", "Предпочтение размещения относительно других групп: Max = подальше, Min = поближе, Any = без разницы")]
		public PlaceMonuments.DistanceMode distanceDifferent;

		[Desc("Min distance to monuments of other groups, meters", "Мин. расстояние до монументов других групп, в метрах")]
		[Schema(Min = 0.0)]
		public int MinDistanceDifferentType;

		[Desc("Where the monument may stand", "Где может стоять монумент")]
		public SpawnFilterCfg Filter = new SpawnFilterCfg();

		[Desc("Different path inside the game bundles (not on disk), relative to assets/bundled/prefabs/autospawn/, comma separated. Empty = vanilla", "Другой путь в бандлах игры (не на диске), относительно assets/bundled/prefabs/autospawn/, через запятую. Пусто = стандартный")]
		public string OverrideFolder = "";

		[Desc("Keep only prefabs whose name (without folder) contains one of these strings, e.g. \"harbor_1\". Empty = all", "Оставить только префабы, в имени которых (без папки) есть одна из строк, например \"harbor_1\". Пусто = все")]
		[Schema(Suggest = "prefabs")]
		public List<string> IncludePrefabs = new List<string>();

		[Desc("Remove prefabs whose name contains one of these strings", "Убрать префабы, в имени которых есть одна из строк")]
		[Schema(Suggest = "prefabs")]
		public List<string> ExcludePrefabs = new List<string>();

		[Desc("\"part of name\": N - how many copies of the prefab go to the candidate pool, 0 = none", "\"часть имени\": N - сколько копий префаба попадёт в пул кандидатов, 0 = ни одной")]
		[Schema(Min = 0.0, Suggest = "prefabs")]
		public Dictionary<string, int> PrefabCopies = new Dictionary<string, int>();

		[Desc("true = place exactly TargetCount (vanilla scales it by map size, curve defined only up to 6000)", "true = ставить ровно TargetCount (игра умножает его на коэффициент размера карты, заданный только до 6000)")]
		public bool IgnoreWorldSizeMultiplier;

		[JsonIgnore]
		public bool HasPrefabRules
		{
			get
			{
				if (IncludePrefabs.Count <= 0 && ExcludePrefabs.Count <= 0)
				{
					return PrefabCopies.Count > 0;
				}
				return true;
			}
		}
	}

	public class SpawnFilterCfg
	{
		[Desc("true = use this filter instead of the vanilla one", "true = использовать этот фильтр вместо стандартного")]
		public bool Enabled;

		[Desc("Allowed ground textures, empty = any", "Разрешённые текстуры земли, пусто = любые")]
		[Schema(Enum = typeof(Enum))]
		public List<string> SplatType = new List<string>();

		[Desc("Allowed biomes, empty = any", "Разрешённые биомы, пусто = любые")]
		[Schema(Enum = typeof(Enum))]
		public List<string> BiomeType = new List<string>();

		[Desc("At least one of these topologies, empty = any", "Хотя бы одна из этих топологий, пусто = любая")]
		[Schema(Enum = typeof(Enum))]
		public List<string> TopologyAny = new List<string>();

		[Desc("All of these topologies, empty = no condition", "Все эти топологии, пусто = без условия")]
		[Schema(Enum = typeof(Enum))]
		public List<string> TopologyAll = new List<string>();

		[Desc("None of these topologies, empty = no condition", "Ни одной из этих топологий, пусто = без условия")]
		[Schema(Enum = typeof(Enum))]
		public List<string> TopologyNot = new List<string>();
	}

	public class SimplePath
	{
		[Desc("Master switch: false = vanilla, other fields are ignored", "Главный переключатель: false = как в игре, остальные поля игнорируются")]
		public bool ShouldChange = true;

		[Desc("The ring. false = no ring on any map size", "Кольцо. false = без кольца на любом размере карты")]
		public bool Enabled = true;

		[Desc("Generate the ring on any map size (vanilla: large maps only)", "Генерировать кольцо на любом размере карты (в игре - только на больших)")]
		public bool GenerateRing = true;

		[Desc("Roadside/railside monuments: gas stations, supermarkets, stations, etc.", "Монументы у дороги/железной дороги: заправки, супермаркеты, станции и т.д.")]
		public bool GenerateSideMonuments = true;

		[Desc("Road only: roadside objects. Does nothing for Rail", "Только для Road: придорожные объекты. Для Rail ни на что не влияет")]
		public bool GenerateSideObjects;
	}

	public class UniqueEnviroment
	{
		[Desc("Master switch: false = vanilla (only on maps 4000-4500+)", "Главный переключатель: false = как в игре (только на картах от 4000-4500)")]
		public bool ShouldChange = true;

		public bool GenerateOasis = true;

		public bool GenerateCanyons = true;

		public bool GenerateLakes = true;
	}

	public sealed class TierSettings
	{
		[Schema(Min = 0.0)]
		public float Tier0 = 30f;

		[Schema(Min = 0.0)]
		public float Tier1 = 30f;

		[Schema(Min = 0.0)]
		public float Tier2 = 40f;
	}

	public sealed class BiomSettings
	{
		[Schema(Min = 0.0)]
		public float Arid = 40f;

		[Schema(Min = 0.0)]
		public float Temperate = 15f;

		[Schema(Min = 0.0)]
		public float Tundra = 15f;

		[Schema(Min = 0.0)]
		public float Arctic = 30f;

		[Desc("Separate from the others, 0-100", "Отдельно от остальных, 0-100")]
		[Schema(Min = 0.0, Max = 100.0)]
		public float Jungle = 50f;
	}

	public sealed class TempData
	{
		public uint mapsize;

		public uint mapseed;

		public bool mapGenerated;

		public TerrainTexturing terrainTexturing;

		public TerrainMeta terrainMeta;

		public TerrainPath terrainPath;

		public List<KeyValuePair<string, Vector3>> customMonuments = new List<KeyValuePair<string, Vector3>>();
	}

	public static ConfigData Config;

	public static TempData tempData;

	private static readonly string CurrentVersion;

	private static readonly string Location;

	private static readonly string SchemaLocation;

	private static readonly string EditorLocation;

	static ExtConfig()
	{
		CurrentVersion = "0.3.0";
		Location = Paths.Get("HarmonyConfig", "CustomGenerator.json");
		SchemaLocation = Paths.Get("HarmonyConfig", "CustomGenerator.schema.json");
		EditorLocation = Paths.Get("HarmonyConfig", "CustomGenerator.editor.html");
		LoadConfig();
	}

	private static JsonSerializerSettings SerializerSettings(string language)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Expected O, but got Unknown
		return new JsonSerializerSettings
		{
			ContractResolver = (IContractResolver)(object)new LocalizedContractResolver(language),
			ObjectCreationHandling = (ObjectCreationHandling)2,
			Formatting = (Formatting)1
		};
	}

	private static string DetectLanguage()
	{
		if (!(CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "ru"))
		{
			return "en";
		}
		return "ru";
	}

	private static void LoadConfig()
	{
		tempData = new TempData();
		string[] array = new string[3]
		{
			Paths.Get("HarmonyConfig"),
			Paths.Get("maps", "custom"),
			Paths.Get("maps", "prefabs")
		};
		for (int i = 0; i < array.Length; i++)
		{
			Directory.CreateDirectory(array[i]);
		}
		if (!File.Exists(Location))
		{
			Logging.Info("Config file not found, creating the default one");
			LoadDefaultConfig();
		}
		else
		{
			try
			{
				string text = File.ReadAllText(Location);
				Config = Deserialize(text);
				if (Config.Version != CurrentVersion)
				{
					string text2 = Location + "." + Config.Version + ".backup";
					File.WriteAllText(text2, text);
					Logging.Config("Config version " + Config.Version + " -> " + CurrentVersion + ", backup saved to " + text2);
					Config.Version = CurrentVersion;
				}
				SaveConfig();
				Logging.Config("Configuration loaded successfully");
			}
			catch (Exception ex)
			{
				Logging.Error("Failed to load configuration", ex);
				string text3 = Location + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}";
				File.Copy(Location, text3, overwrite: true);
				Logging.Config("Broken config saved to " + text3 + ", loading default configuration...");
				LoadDefaultConfig();
			}
		}
		Validate();
	}

	private static ConfigData Deserialize(string raw)
	{
		string language = ((JToken)JObject.Parse(raw)).Value<string>((object)"Language (en/ru)") ?? DetectLanguage();
		return JsonConvert.DeserializeObject<ConfigData>(raw, SerializerSettings(language));
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
		SaveConfig(Config);
	}

	private static void SaveConfig(ConfigData data)
	{
		string text = null;
		try
		{
			text = JsonConvert.SerializeObject((object)data, SerializerSettings(data.Language));
			File.WriteAllText(Location, text);
			Logging.Config("Configuration saved successfully");
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to save configuration", ex);
		}
		SaveSchema(data, text);
	}

	public static void MergeMonumentGroups(List<Monument> found)
	{
		List<Monument> missing;
		List<Monument> list = MergeGroups(Config.Monuments.monuments, found, out missing);
		foreach (Monument item in missing)
		{
			Logging.Warning("Monument group '" + item.Description + "' (" + item.Folder + ") is not in this Rust version, kept in the config but has no effect");
		}
		if (list.Count != 0)
		{
			Logging.Config(string.Format("Added {0} monument groups to the config: {1}", list.Count, string.Join(", ", list.Select((Monument x) => x.Description))));
			ConfigData configData;
			try
			{
				configData = Deserialize(File.ReadAllText(Location));
			}
			catch (Exception ex)
			{
				Logging.Error("Failed to re-read the config, monument groups not saved", ex);
				return;
			}
			MergeGroups(configData.Monuments.monuments, found.Select((Monument x) => JsonConvert.DeserializeObject<Monument>(JsonConvert.SerializeObject((object)x))).ToList(), out var _);
			SaveConfig(configData);
		}
	}

	private static List<Monument> MergeGroups(List<Monument> list, List<Monument> found, out List<Monument> missing)
	{
		missing = new List<Monument>(list);
		List<Monument> list2 = new List<Monument>();
		List<Monument> list3 = new List<Monument>();
		foreach (Monument group in found)
		{
			Monument monument = missing.FirstOrDefault((Monument x) => x.Folder == group.Folder);
			if (monument != null)
			{
				missing.Remove(monument);
				list3.Add(monument);
			}
			else
			{
				list2.Add(group);
				list3.Add(group);
			}
		}
		list3.AddRange(missing);
		list.Clear();
		list.AddRange(list3);
		return list2;
	}

	private static void SaveSchema(ConfigData data, string json)
	{
		try
		{
			JObject val = ConfigSchema.Build(typeof(ConfigData), (IContractResolver)(object)new LocalizedContractResolver(data.Language), data.Language == "ru");
			File.WriteAllText(SchemaLocation, ((JToken)val).ToString((Formatting)1, Array.Empty<JsonConverter>()));
			if (json != null)
			{
				ConfigEditor.Write(EditorLocation, val, json, data.CustomMonuments?.Folder);
			}
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to save config schema or editor", ex);
		}
	}

	private static void Validate()
	{
		GeneratorSettings generator = Config.Generator;
		if (Config.Language != "en" && Config.Language != "ru")
		{
			Logging.Warning("Unknown language '" + Config.Language + "', expected 'en' or 'ru'");
		}
		try
		{
			string.Format(Config.mapSettings.MapName, 0, 0);
		}
		catch (FormatException)
		{
			Logging.Warning("Map name '" + Config.mapSettings.MapName + "' has invalid placeholders (only {0} and {1} allowed), using default");
			Config.mapSettings.MapName = new MapSettings().MapName;
		}
		ConfigData config = Config;
		MapImageSettings mapImageSettings = config.MapImage ?? (config.MapImage = new MapImageSettings());
		if (mapImageSettings.Scale < 0.1f || mapImageSettings.Scale > 4f)
		{
			Logging.Warning($"Map image scale must be 0.1-4 (got {mapImageSettings.Scale}), using {Mathf.Clamp(mapImageSettings.Scale, 0.1f, 4f)}");
			mapImageSettings.Scale = Mathf.Clamp(mapImageSettings.Scale, 0.1f, 4f);
		}
		if (mapImageSettings.OceanMargin < 0)
		{
			Logging.Warning($"Map image ocean margin must be >= 0 (got {mapImageSettings.OceanMargin}), using 0");
			mapImageSettings.OceanMargin = 0;
		}
		if (generator.RiverWidthScale <= 0f)
		{
			Logging.Warning($"River width scale must be > 0 (got {generator.RiverWidthScale}), using 1");
			generator.RiverWidthScale = 1f;
		}
		if (generator.ModifyPercentages)
		{
			float num = generator.Tier.Tier0 + generator.Tier.Tier1 + generator.Tier.Tier2;
			float num2 = generator.Biom.Arid + generator.Biom.Temperate + generator.Biom.Tundra + generator.Biom.Arctic;
			if (new float[8]
			{
				generator.Tier.Tier0,
				generator.Tier.Tier1,
				generator.Tier.Tier2,
				generator.Biom.Arid,
				generator.Biom.Temperate,
				generator.Biom.Tundra,
				generator.Biom.Arctic,
				generator.Biom.Jungle
			}.Any((float x) => x < 0f) || num <= 0f || num2 <= 0f)
			{
				Logging.Warning("Tier/biome percentages contain negative values or sum to 0, keeping vanilla percentages");
				generator.ModifyPercentages = false;
			}
			else
			{
				if (Mathf.Abs(num - 100f) > 0.01f)
				{
					Logging.Warning($"Tier percentages sum to {num}, not 100 - they will be scaled proportionally");
				}
				if (Mathf.Abs(num2 - 100f) > 0.01f)
				{
					Logging.Warning($"Biome percentages (without Jungle) sum to {num2}, not 100 - they will be scaled proportionally");
				}
				if (generator.Biom.Jungle > 100f)
				{
					Logging.Warning($"Jungle percentage {generator.Biom.Jungle} is over 100, using 100");
					generator.Biom.Jungle = 100f;
				}
			}
		}
		foreach (Monument monument3 in Config.Monuments.monuments)
		{
			string text = (string.IsNullOrEmpty(monument3.Description) ? monument3.Folder : monument3.Description);
			if (monument3.TargetCount < 0 || monument3.MinWorldSize < 0 || monument3.MinDistanceSameType < 0 || monument3.MinDistanceDifferentType < 0)
			{
				Logging.Warning("Monument '" + text + "': negative count/size/distance replaced with 0");
				monument3.TargetCount = Math.Max(0, monument3.TargetCount);
				monument3.MinWorldSize = Math.Max(0, monument3.MinWorldSize);
				monument3.MinDistanceSameType = Math.Max(0, monument3.MinDistanceSameType);
				monument3.MinDistanceDifferentType = Math.Max(0, monument3.MinDistanceDifferentType);
			}
			monument3.OverrideFolder = (monument3.OverrideFolder ?? "").Trim();
			monument3.IncludePrefabs = (from x in monument3.IncludePrefabs ?? new List<string>()
				where !string.IsNullOrWhiteSpace(x)
				select x.Trim().ToLowerInvariant()).ToList();
			monument3.ExcludePrefabs = (from x in monument3.ExcludePrefabs ?? new List<string>()
				where !string.IsNullOrWhiteSpace(x)
				select x.Trim().ToLowerInvariant()).ToList();
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			foreach (KeyValuePair<string, int> item in monument3.PrefabCopies ?? new Dictionary<string, int>())
			{
				if (!string.IsNullOrWhiteSpace(item.Key))
				{
					if (item.Value < 0)
					{
						Logging.Warning("Monument '" + text + "': negative copies for '" + item.Key + "' replaced with 0");
					}
					dictionary[item.Key.Trim().ToLowerInvariant()] = Math.Max(0, item.Value);
				}
			}
			monument3.PrefabCopies = dictionary;
			Monument monument = monument3;
			SpawnFilterCfg obj = monument.Filter ?? (monument.Filter = new SpawnFilterCfg());
			obj.SplatType = ValidEnumNames<Enum>(obj.SplatType, text, "SplatType");
			obj.BiomeType = ValidEnumNames<Enum>(obj.BiomeType, text, "BiomeType");
			obj.TopologyAny = ValidEnumNames<Enum>(obj.TopologyAny, text, "TopologyAny");
			obj.TopologyAll = ValidEnumNames<Enum>(obj.TopologyAll, text, "TopologyAll");
			obj.TopologyNot = ValidEnumNames<Enum>(obj.TopologyNot, text, "TopologyNot");
		}
		config = Config;
		CustomMonumentSettings customMonumentSettings = config.CustomMonuments ?? (config.CustomMonuments = new CustomMonumentSettings());
		CustomMonumentSettings customMonumentSettings2 = customMonumentSettings;
		if (customMonumentSettings2.List == null)
		{
			customMonumentSettings2.List = new List<CustomMonument>();
		}
		if (string.IsNullOrWhiteSpace(customMonumentSettings.Folder))
		{
			customMonumentSettings.Folder = new CustomMonumentSettings().Folder;
		}
		foreach (CustomMonument monument2 in customMonumentSettings.List)
		{
			string text2 = (string.IsNullOrEmpty(monument2.Name) ? monument2.File : monument2.Name);
			if (string.IsNullOrWhiteSpace(monument2.File))
			{
				Logging.Warning("Custom monument '" + text2 + "': File is empty, disabled");
				monument2.Enabled = false;
			}
			else if (customMonumentSettings.Enabled && monument2.Enabled && !File.Exists(Path.Combine(Paths.Resolve(customMonumentSettings.Folder), monument2.File)))
			{
				Logging.Warning("Custom monument '" + text2 + "': file " + Path.Combine(Paths.Resolve(customMonumentSettings.Folder), monument2.File) + " not found, disabled");
				monument2.Enabled = false;
			}
			string[] array = new string[3] { "Stamp", "Flatten", "None" };
			string text3 = array.FirstOrDefault((string x) => string.Equals(x, monument2.HeightMode?.Trim(), StringComparison.OrdinalIgnoreCase));
			if (text3 == null)
			{
				Logging.Warning("Custom monument '" + text2 + "': unknown HeightMode '" + monument2.HeightMode + "' (valid: " + string.Join(", ", array) + "), using Stamp");
			}
			monument2.HeightMode = text3 ?? "Stamp";
			if (monument2.Count < 0 || monument2.Radius < 0f || monument2.Blend < 0f || monument2.MaxHeightDifference < 0f || monument2.MinDistanceToMonuments < 0 || monument2.MinDistanceSameType < 0)
			{
				Logging.Warning("Custom monument '" + text2 + "': negative values replaced with 0");
				monument2.Count = Math.Max(0, monument2.Count);
				monument2.Radius = Math.Max(0f, monument2.Radius);
				monument2.Blend = Math.Max(0f, monument2.Blend);
				monument2.MaxHeightDifference = Math.Max(0f, monument2.MaxHeightDifference);
				monument2.MinDistanceToMonuments = Math.Max(0, monument2.MinDistanceToMonuments);
				monument2.MinDistanceSameType = Math.Max(0, monument2.MinDistanceSameType);
			}
			CustomMonument customMonument2;
			if (monument2.MinHeight > monument2.MaxHeight)
			{
				Logging.Warning("Custom monument '" + text2 + "': MinHeight > MaxHeight, values swapped");
				CustomMonument customMonument = monument2;
				customMonument2 = monument2;
				float maxHeight = monument2.MaxHeight;
				float minHeight = monument2.MinHeight;
				customMonument.MinHeight = maxHeight;
				customMonument2.MaxHeight = minHeight;
			}
			customMonument2 = monument2;
			SpawnFilterCfg obj2 = customMonument2.Filter ?? (customMonument2.Filter = new SpawnFilterCfg());
			obj2.SplatType = ValidEnumNames<Enum>(obj2.SplatType, text2, "SplatType");
			obj2.BiomeType = ValidEnumNames<Enum>(obj2.BiomeType, text2, "BiomeType");
			obj2.TopologyAny = ValidEnumNames<Enum>(obj2.TopologyAny, text2, "TopologyAny");
			obj2.TopologyAll = ValidEnumNames<Enum>(obj2.TopologyAll, text2, "TopologyAll");
			obj2.TopologyNot = ValidEnumNames<Enum>(obj2.TopologyNot, text2, "TopologyNot");
		}
	}

	private static List<string> ValidEnumNames<T>(List<string> values, string monument, string field) where T : struct, Enum
	{
		if (values == null)
		{
			return new List<string>();
		}
		List<string> list = new List<string>();
		foreach (string value in values)
		{
			if (Enum.TryParse<T>(value?.Trim(), out var _))
			{
				list.Add(value.Trim());
				continue;
			}
			Logging.Warning("Monument '" + monument + "': unknown " + field + " value '" + value + "' ignored (valid: " + string.Join(", ", Enum.GetNames(typeof(T))) + ")");
		}
		return list;
	}
}
