using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BagCooldowns;

public class HarmonyConfig
{
	public class ConfigData
	{
		public readonly RespawnOption SleepingBag = new RespawnOption
		{
			UnlockSeconds = 150f,
			SecondsBetweenReuses = 150f
		};

		public readonly RespawnOption Bed = new RespawnOption
		{
			UnlockSeconds = 60f,
			SecondsBetweenReuses = 60f
		};

		public readonly RespawnOption BeachTowel = new RespawnOption
		{
			UnlockSeconds = 150f,
			SecondsBetweenReuses = 150f
		};

		public readonly RespawnOption Camper = new RespawnOption
		{
			UnlockSeconds = 150f,
			SecondsBetweenReuses = 150f
		};
	}

	public class RespawnOption
	{
		public float UnlockSeconds;

		public float SecondsBetweenReuses;
	}

	public static ConfigData Config;

	private static readonly string Location = Path.Combine("HarmonyConfig", "BagCooldowns.json");

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
