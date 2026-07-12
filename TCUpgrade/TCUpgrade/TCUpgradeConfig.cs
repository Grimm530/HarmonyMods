using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TCUpgrade;

public class TCUpgradeConfig
{
	public class ConfigData
	{
		[JsonProperty("Admin Steam IDs (bypass all permission checks)")]
		public List<ulong> AdminSteamIds = new List<ulong>();

		[JsonProperty("Grant upgrade/skin item permissions to all players (false = only admins can use skin tiers)")]
		public bool GrantUpgradeSkinPermissionsToAll = true;

		[JsonProperty("Bypass DLC ownership check")]
		public bool AllowAllSkins;

		[JsonProperty("Use NoEscape Plugin")]
		public bool UseNoEscape = true;

		[JsonProperty("Use RaidBlock Plugin")]
		public bool UseRaidBlock = true;

		[JsonProperty("Debug (verbose logging for troubleshooting)")]
		public bool Debug;

		[JsonProperty("Images Path Override (e.g. C:\\!Grimmzone\\HarmonyImages\\TCUpgrade, empty = server root/HarmonyImages/TCUpgrade)")]
		public string ImagesPathOverride = "";

		[JsonProperty("Image URL Base (e.g. https://yourserver.com/tcupgrade/, avoids FileStorage; empty = use local images)")]
		public string ImageUrlBase = "";

		[JsonProperty("Use URL for menu images (when Img is a short key, use ImageUrlBase; false = use FileStorage/HarmonyImages)")]
		public bool UseUrlForMenuImages;

		[JsonProperty("GUI Buttons TC - Color Default")]
		public string BtnTcColor = "0.3 0.40 0.3 0.60";

		[JsonProperty("GUI Buttons TC - Color Active")]
		public string BtnTcColorActive = "0.90 0.20 0.20 0.50";

		[JsonProperty("GUI Buttons TC - OffsetMin")]
		public string OffsetMin = "278 621";

		[JsonProperty("GUI Buttons TC - OffsetMax")]
		public string OffsetMax = "571 643";

		[JsonProperty("GUI Buttons TC - AnchorMin")]
		public string AnchorMin = "0.5 0";

		[JsonProperty("GUI Buttons TC - AnchorMax")]
		public string AnchorMax = "0.5 0";

		[JsonProperty("GUI Buttons TC - CUI Parent (Overlay, OverlayNonScaled, or Hud; try Hud if buttons unclickable)")]
		public string ButtonsParent = "OverlayNonScaled";

		[JsonProperty("Alert Gametip")]
		public bool AlertGametip = true;

		[JsonProperty("Alert Chat")]
		public bool AlertChat = true;

		[JsonProperty("Color Prefix Chat")]
		public string ColorPrefix = "#f74d31";

		[JsonProperty("Show Admin Auth List")]
		public bool Adminshow;

		[JsonProperty("Show SteamID Auth List")]
		public bool SteamIdShow = true;

		[JsonProperty("Show player status in auth list (Online/Offline for Admin Steam IDs)")]
		public bool ShowPlayerStatusInAuthList;

		[JsonProperty("Upgrade Effect")]
		public bool PlayFx = true;

		[JsonProperty("Colour Selection MultiColor Option")]
		public bool EnableMultiColor = true;

		[JsonProperty("Reskin Enable")]
		public bool Reskin = true;

		[JsonProperty("Reskin Wall Enable")]
		public bool ReskinWall = true;

		[JsonProperty("Only reskin on wall of the same grade")]
		public bool SameWallGrade = true;

		[JsonProperty("Reskin Wall TC Distance (Default: 100)")]
		public float UpwallDis = 100f;

		[JsonProperty("Deployables Repair")]
		public bool Deployables = true;

		[JsonProperty("Repair Cooldown After Recent Damage (seconds)")]
		public float RepairCooldown = 30f;

		[JsonProperty("Downgrade Enable")]
		public bool Downgrade = true;

		[JsonProperty("Downgrade only Owner Entity Build")]
		public bool OnlyOwner = true;

		[JsonProperty("Upgrade only Owner Entity Build")]
		public bool OnlyOwnerUp = true;

		[JsonProperty("Upgrade / Downgrade only Owner and Team")]
		public bool TeamUpdate;

		[JsonProperty("Wallpaper Enable")]
		public bool Wallpaper = true;

		[JsonProperty("Wallpaper placement Cost (Cloth)")]
		public int WallResource = 5;

		[JsonProperty("Wallpaper both sides")]
		public bool BothSides = true;

		[JsonProperty("Force both sides including external sides (no longer patches game CheckWallpaper; game may remove invalid wallpaper)")]
		public bool ForceBothSides = true;

		[JsonProperty("Cooldown Frequency Upgrade (larger number is slower)")]
		public Dictionary<string, float> FrequencyUpgrade = new Dictionary<string, float>
		{
			["TCUpgrade.use"] = 2f,
			["TCUpgrade.vip"] = 1f
		};

		[JsonProperty("Cooldown Frequency Repair (larger number is slower)")]
		public Dictionary<string, float> FrequencyRepair = new Dictionary<string, float>
		{
			["TCUpgrade.use"] = 2f,
			["TCUpgrade.vip"] = 1f
		};

		[JsonProperty("Cooldown Frequency Reskin (larger number is slower)")]
		public Dictionary<string, float> FrequencyReskin = new Dictionary<string, float>
		{
			["TCUpgrade.use"] = 2f,
			["TCUpgrade.vip"] = 1f
		};

		[JsonProperty("Cooldown Frequency Wallpaper (larger number is slower)")]
		public Dictionary<string, float> FrequencyWallpaper = new Dictionary<string, float>
		{
			["TCUpgrade.use"] = 2f,
			["TCUpgrade.vip"] = 1f
		};

		[JsonProperty("Cost Modifier for repairs")]
		public Dictionary<string, float> CostListRepair = new Dictionary<string, float>
		{
			["TCUpgrade.use"] = 1.5f,
			["TCUpgrade.vip"] = 1f
		};

		[JsonProperty("Allow Items in TC Inventory")]
		public Dictionary<string, bool> AllowedItemsConfig = new Dictionary<string, bool>
		{
			["gunpowder"] = false,
			["sulfur"] = false,
			["sulfur.ore"] = false,
			["explosives"] = false,
			["diesel_barrel"] = false,
			["cctv.camera"] = false,
			["targeting.computer"] = false
		};

		[JsonProperty("Auto Sort Items by Grade")]
		public bool AutoSortItems = true;

		[JsonProperty("Items")]
		public List<ItemInfo> ItemsList = new List<ItemInfo>();
	}

	public class ItemInfo
	{
		[JsonProperty("ID")]
		public int ID;

		[JsonProperty("Enabled")]
		public bool Enabled = true;

		[JsonProperty("Short Name")]
		public string Name = "";

		[JsonProperty("Grade")]
		public string Grade = "wood";

		[JsonProperty("Img Icon")]
		public string Img = "";

		[JsonProperty("ItemID")]
		public int ItemID;

		[JsonProperty("SkinID")]
		public int SkinId;

		[JsonProperty("Color")]
		public bool Color;

		[JsonProperty("Wall")]
		public int Wall;

		[JsonProperty("ItemID2")]
		public int ItemID2;

		[JsonProperty("Permission Use")]
		public string Permission = "TCUpgrade.use";

		[JsonProperty("Disable for Barges")]
		public bool DisableBarges;
	}

	public static ConfigData Config;

	private static string _configPath;

	private static void MigrateDefaultItems()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Wood"] = "wood",
			["Legacy Wood"] = "legacywood",
			["Gingerbread"] = "gingerbread",
			["Stone"] = "stone",
			["Adobe"] = "adobe",
			["Brick"] = "brick",
			["Brutalist"] = "brutalist",
			["Metal"] = "metal",
			["Container"] = "container",
			["Armored"] = "armored",
			["Jungle"] = "jungle",
			["Space Station"] = "spacetest",
			["Crypt"] = "crypt",
			["Cryp"] = "crypt"
		};
		bool flag = false;
		foreach (ItemInfo items in Config.ItemsList)
		{
			if (items.SkinId == 10472 && string.Equals(items.Name?.Trim(), "Cryp", StringComparison.OrdinalIgnoreCase))
			{
				items.Name = "Crypt";
				flag = true;
			}
			if (string.IsNullOrEmpty(items.Img) && !string.IsNullOrEmpty(items.Name) && dictionary.TryGetValue(items.Name.Trim(), out var value))
			{
				items.Img = value;
				flag = true;
			}
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (ItemInfo items2 in Config.ItemsList)
		{
			hashSet.Add(items2.ID);
		}
		foreach (ItemInfo defaultItem in GetDefaultItems())
		{
			if (hashSet.Add(defaultItem.ID))
			{
				Config.ItemsList.Add(defaultItem);
				flag = true;
			}
		}
		if (flag)
		{
			SaveConfig();
			Debug.Log((object)"[TCUpgrade] Config: synced missing default items and Img Icon values for upgrade menu images (HarmonyImages/TCUpgrade).");
		}
	}

	private static List<ItemInfo> GetDefaultItems()
	{
		return new List<ItemInfo>
		{
			new ItemInfo
			{
				ID = 1,
				Name = "Wood",
				Grade = "wood",
				Img = "wood",
				ItemID = -151838493,
				SkinId = 0,
				Color = false,
				Wall = 0,
				ItemID2 = 99588025,
				Permission = "TCUpgrade.updefault"
			},
			new ItemInfo
			{
				ID = 2,
				Name = "Legacy Wood",
				Grade = "wood",
				Img = "legacywood",
				ItemID = 0,
				SkinId = 10232,
				Color = false,
				Wall = 10302,
				ItemID2 = -1993883724,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 3,
				Name = "Gingerbread",
				Grade = "wood",
				Img = "gingerbread",
				ItemID = 0,
				SkinId = 2,
				Color = false,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 4,
				Name = "Stone",
				Grade = "stone",
				Img = "stone",
				ItemID = -2099697608,
				SkinId = 0,
				Color = false,
				Wall = 1,
				ItemID2 = -691113464,
				Permission = "TCUpgrade.updefault"
			},
			new ItemInfo
			{
				ID = 5,
				Name = "Adobe",
				Grade = "stone",
				Img = "adobe",
				ItemID = 0,
				SkinId = 10220,
				Color = false,
				Wall = 10304,
				ItemID2 = -401905610,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 6,
				Name = "Brick",
				Grade = "stone",
				Img = "brick",
				ItemID = 0,
				SkinId = 10223,
				Color = false,
				Wall = 2,
				ItemID2 = -985781766,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 7,
				Name = "Brutalist",
				Grade = "stone",
				Img = "brutalist",
				ItemID = 0,
				SkinId = 10225,
				Color = false,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 8,
				Name = "Metal",
				Grade = "metal",
				Img = "metal",
				ItemID = 69511070,
				SkinId = 0,
				Color = false,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.updefault"
			},
			new ItemInfo
			{
				ID = 9,
				Name = "Container",
				Grade = "metal",
				Img = "container",
				ItemID = 0,
				SkinId = 10221,
				Color = true,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 10,
				Name = "Armored",
				Grade = "armored",
				Img = "armored",
				ItemID = 317398316,
				SkinId = 0,
				Color = false,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.updefault"
			},
			new ItemInfo
			{
				ID = 11,
				Name = "Jungle",
				Grade = "stone",
				Img = "jungle",
				ItemID = 0,
				SkinId = 10326,
				Color = false,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 12,
				Name = "Space Station",
				Grade = "armored",
				Img = "spacetest",
				ItemID = 0,
				SkinId = 10430,
				Color = false,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.upskin"
			},
			new ItemInfo
			{
				ID = 13,
				Name = "Crypt",
				Grade = "stone",
				Img = "crypt",
				ItemID = 0,
				SkinId = 10472,
				Color = false,
				Wall = -1,
				ItemID2 = 0,
				Permission = "TCUpgrade.upskin"
			}
		};
	}

	public static void LoadConfig()
	{
		try
		{
			string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			string path = Path.Combine(fullPath, "oxide", "config", "TCUpgrade.json");
			string configPath = Path.Combine(fullPath, "HarmonyConfig", "TCUpgrade.json");
			string path2 = Path.Combine(fullPath, "HarmonyConfig", "TCUpgrade.json");
			string path3 = Path.Combine(fullPath, "oxide", "config", "TCUpgrade.json");
			_configPath = configPath;
			string directoryName = Path.GetDirectoryName(_configPath);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			if (File.Exists(_configPath))
			{
				Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(_configPath));
				Debug.Log((object)("[TCUpgrade] Config loaded from " + _configPath));
			}
			else if (File.Exists(path2))
			{
				Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path2));
				if (Config != null)
				{
					SaveConfig();
					Debug.Log((object)("[TCUpgrade] Config migrated from TCUpgrade.json at " + _configPath));
				}
			}
			else if (File.Exists(path))
			{
				Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
				if (Config != null)
				{
					SaveConfig();
					Debug.Log((object)("[TCUpgrade] Config created from oxide/config at " + _configPath));
				}
			}
			else if (File.Exists(path3))
			{
				Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path3));
				if (Config != null)
				{
					SaveConfig();
					Debug.Log((object)("[TCUpgrade] Config migrated from oxide TCUpgrade.json at " + _configPath));
				}
			}
			if (Config == null)
			{
				Config = new ConfigData();
			}
			ConfigData config = Config;
			if (config.ItemsList == null)
			{
				config.ItemsList = new List<ItemInfo>();
			}
			if (Config.ItemsList.Count == 0)
			{
				Config.ItemsList.AddRange(GetDefaultItems());
				SaveConfig();
				Debug.Log((object)("[TCUpgrade] Config created with full Items list at " + _configPath));
			}
			else
			{
				MigrateDefaultItems();
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[TCUpgrade] Config load error: " + ex.Message));
			if (Config == null)
			{
				Config = new ConfigData();
			}
		}
	}

	public static void SaveConfig()
	{
		try
		{
			if (_configPath != null && Config != null)
			{
				string directoryName = Path.GetDirectoryName(_configPath);
				if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				string contents = JToken.Parse(JsonConvert.SerializeObject((object)Config)).ToString((Formatting)1, Array.Empty<JsonConverter>());
				File.WriteAllText(_configPath, contents);
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[TCUpgrade] Config save error: " + ex.Message + "\nPath: " + _configPath + "\n" + ex.StackTrace));
		}
	}
}
