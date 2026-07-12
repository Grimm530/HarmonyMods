using System.IO;
using Newtonsoft.Json;

namespace CraftingSpeed;

public class HarmonyConfig
{
	public class ConfigData
	{
		public float CraftingSpeedMultiplier = 2f;
	}

	public static ConfigData Config;

	private static readonly string Location = Path.Combine("HarmonyConfig", "CraftingSpeed.json");

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
		File.WriteAllText(Location, JsonConvert.SerializeObject((object)Config));
	}
}
