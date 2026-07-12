using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MixingSpeed;

public class HarmonyConfig
{
	public class ConfigData
	{
		public float MixingSpeedMultiplier = 2f;

		public bool InstantMix = false;
	}

	public static ConfigData Config;

	private static readonly string Location = Path.Combine("HarmonyConfig", "MixingSpeed.json");

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
		}
		catch
		{
			LoadDefaultConfig();
		}
	}

	private static void LoadDefaultConfig()
	{
		Config = new ConfigData();
		File.WriteAllText(Location, JToken.Parse(JsonConvert.SerializeObject((object)Config)).ToString((Formatting)1, Array.Empty<JsonConverter>()));
	}
}
