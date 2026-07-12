using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using StackManager.Config;
using UnityEngine;

namespace StackManager.Utility;

public class Settings
{
	public static DefaultConfig Config { get; private set; }

	private static readonly string Home = Path.Combine(Application.dataPath, "..", "HarmonyConfig");

	private static readonly string Location = Path.Combine(Home, "StackManager.json");

	private static readonly string OldLocation = Path.Combine(Application.dataPath, "..", "HarmonyMods", "StackManager.json");

	/// <summary>Load config from file and store in cache. Called on mod load. Always reads from disk.</summary>
	public static void LoadConfig()
	{
		if (!Directory.Exists(Home))
		{
			Directory.CreateDirectory(Home);
		}
		// Migrate config from old HarmonyMods location if it exists
		if (!File.Exists(Location) && File.Exists(OldLocation))
		{
			File.Copy(OldLocation, Location);
			Log.Information("Configuration migrated from HarmonyMods to HarmonyConfig.");
		}
		try
		{
			Config = JsonConvert.DeserializeObject<DefaultConfig>(File.ReadAllText(Location));
			if (Config.ItemExact == null)
				Config.ItemExact = new Dictionary<string, int>();
			Log.Information("Configuration file was successfully loaded.");
		}
		catch
		{
			LoadDefaultConfig();
		}
	}

	public static void SaveConfig()
	{
		if (Config == null)
			return;
		try
		{
			File.WriteAllText(Location, JsonConvert.SerializeObject((object)Config, (Formatting)1));
		}
		catch
		{
			Log.Error("Unknown error while saving the configuration file.");
		}
	}

	/// <summary>Clear config cache on unload. Next load will read fresh from file. Do not save—would overwrite user edits.</summary>
	internal static void ClearCache()
	{
		Config = null;
	}

	private static void LoadDefaultConfig()
	{
		Log.Warning("A new default configuration file was created at: " + Location);
		Config = new DefaultConfig();
		SaveConfig();
	}
}
