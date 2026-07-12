using System;
using System.IO;
using Newtonsoft.Json;

namespace FakePopulation;

public class FakePopulationConfig
{
	public int BonusPlayers { get; set; } = 30;

	private static readonly string ConfigPath = Path.Combine("HarmonyConfig", "FakePopulation.json");

	public static FakePopulationConfig Load()
	{
		try
		{
			var dir = Path.GetDirectoryName(ConfigPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			if (File.Exists(ConfigPath))
			{
				var json = File.ReadAllText(ConfigPath);
				var loaded = JsonConvert.DeserializeObject<FakePopulationConfig>(json);
				if (loaded != null)
					return loaded;
			}
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogWarning($"[FakePopulation] Config load error: {ex.Message}");
		}

		var config = new FakePopulationConfig();
		config.Save();
		return config;
	}

	public void Save()
	{
		try
		{
			var dir = Path.GetDirectoryName(ConfigPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogWarning($"[FakePopulation] Config save error: {ex.Message}");
		}
	}
}
