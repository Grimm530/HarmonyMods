using System.IO;
using Newtonsoft.Json;

namespace RecyclerSpeed;

public class HarmonyConfig
{
	public class ConfigData
	{
		/// <summary>
		/// Divide recycle interval by this value. 2 = half the time (2x speed).
		/// Affects all recycler types (safezone and radtown). No permissions.
		/// </summary>
		public float RecyclerSpeedMultiplier = 2f;

		/// <summary>
		/// Show CUI overlay over the static "60% EFFICIENCY, 5 SEC" text with actual modded values.
		/// </summary>
		public bool ShowOverlay = true;

		/// <summary>
		/// CUI parent: Overlay, Hud, or OverlayNonScaled. Match TCUpgrade: use OverlayNonScaled for panels.
		/// </summary>
		public string OverlayParent = "OverlayNonScaled";

		/// <summary>
		/// Overlay position (normalized 0-1). Compact strip over the efficiency text.
		/// </summary>
		public string OverlayAnchormin = "0.841 0.386";
		public string OverlayAnchormax = "0.960 0.415";

		/// <summary>
		/// When true, log to server console for debugging overlay issues.
		/// </summary>
		public bool Debug = false;
	}

	public static ConfigData Config;

	private static readonly string Location = Path.Combine("HarmonyConfig", "RecyclerSpeed.json");

	public static void LoadConfig()
	{
		if (!Directory.Exists("HarmonyConfig"))
		{
			Directory.CreateDirectory("HarmonyConfig");
		}
		if (!File.Exists(Location))
		{
			LoadDefaultConfig();
			return;
		}
		try
		{
			Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location));
			if (Config == null)
				LoadDefaultConfig();
		}
		catch
		{
			LoadDefaultConfig();
		}
	}

	private static void LoadDefaultConfig()
	{
		Config = new ConfigData();
		File.WriteAllText(Location, JsonConvert.SerializeObject(Config, Formatting.Indented));
	}

	public static void SaveConfig()
	{
		if (Config == null) return;
		if (!Directory.Exists("HarmonyConfig"))
			Directory.CreateDirectory("HarmonyConfig");
		File.WriteAllText(Location, JsonConvert.SerializeObject(Config, Formatting.Indented));
	}
}
