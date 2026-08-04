using System;
using System.IO;
using System.Reflection;
using FullRangeAutoturrets.Config;
using FullRangeAutoturrets.Lib.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace FullRangeAutoturrets.Lib.Config;

public class ConfigManager
{
	private ConfigFile Configuration;

	private string ConfigPath;

	public event Action OnConfigLoaded;

	public ConfigManager()
	{
		ConfigPath = "HarmonyData/FullRangeAutoturrets/Configuration.json";
	}

	public object Get(string propName, object src = null)
	{
		try
		{
			if (src == null)
			{
				src = Configuration;
			}
			if (propName == null)
			{
				throw new ArgumentException("Value cannot be null.", "propName");
			}
			if (propName.Contains("."))
			{
				string[] array = propName.Split(new char[1] { '.' }, 2);
				return Get(array[1], Get(array[0], src));
			}
			PropertyInfo property = src.GetType().GetProperty(propName);
			return (property != null) ? property.GetValue(src, null) : null;
		}
		catch
		{
		}
		LoggingManager.Log("Failed to get configuration value for key " + propName);
		return null;
	}

	public void Load()
	{
		try
		{
			Configuration = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(ConfigPath)) ?? new ConfigFile();
		}
		catch
		{
			LoggingManager.Log("Failed to load configuration file, loading default configuration.");
			Configuration = new ConfigFile();
			if (File.Exists(ConfigPath))
			{
				return;
			}
		}
		Save();
		if (!(bool)Get("Enabled"))
		{
			LoggingManager.Log("Mod is disabled, please enable it in the configuration file.");
		}
		else
		{
			this.OnConfigLoaded?.Invoke();
		}
	}

	private void Save()
	{
		try
		{
			FileInfo fileInfo = new FileInfo(ConfigPath);
			if (!fileInfo.Directory.Exists)
			{
				fileInfo.Directory.Create();
			}
			File.WriteAllText(ConfigPath, JsonConvert.SerializeObject((object)Configuration, (Formatting)1));
		}
		catch (Exception ex)
		{
			LoggingManager.Log("Could not write to configuration file");
			Debug.LogException(ex);
		}
	}
}
