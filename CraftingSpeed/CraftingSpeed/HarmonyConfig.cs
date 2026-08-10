using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CraftingSpeed;

public class HarmonyConfig
{
	public class ConfigData
	{
		public float CraftingSpeedMultiplier = 2f;
	}

	public static ConfigData Config;

	private static string Location => Path.Combine(ResolveServerRoot(), "HarmonyConfig", "CraftingSpeed.json");
	private static bool _loaded;

	public static void LoadConfig()
	{
		if (_loaded && Config != null) return;

		string dir = Path.Combine(ResolveServerRoot(), "HarmonyConfig");
		if (!Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		if (!File.Exists(Location))
		{
			LoadDefaultConfig();
			_loaded = true;
			return;
		}
		try
		{
			Config = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(Location));
			if (Config == null || Config.CraftingSpeedMultiplier <= 0f)
				LoadDefaultConfig();
		}
		catch
		{
			LoadDefaultConfig();
		}
		_loaded = true;
	}

	private static void LoadDefaultConfig()
	{
		Config = new ConfigData();
		try { File.WriteAllText(Location, JsonConvert.SerializeObject(Config, Formatting.Indented)); }
		catch { }
	}

	private static string ResolveServerRoot()
	{
		try
		{
			if (!string.IsNullOrEmpty(Application.dataPath))
			{
				string fromUnity = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				if (Directory.Exists(Path.Combine(fromUnity, "HarmonyMods"))
					|| Directory.Exists(Path.Combine(fromUnity, "HarmonyConfig")))
					return fromUnity;
			}
		}
		catch { }

		return Path.GetFullPath(Directory.GetCurrentDirectory() ?? ".");
	}
}
