using System;
using System.Reflection;
using FullRangeAutoturrets.Lib.Commands;
using FullRangeAutoturrets.Lib.Config;
using FullRangeAutoturrets.Lib.Logging;
using UnityEngine;

namespace FullRangeAutoturrets;

internal class Main
{
	public static Main instance;

	public bool isLoaded = false;

	public bool isInitialized = false;

	public ConfigManager Config;

	public CommandManager Commands;

	public const string ModName = "Full Range Autoturrets";

	public const string ModShortName = "FullRangeAutoturrets";

	public static string ModVersion;

	public const string ModAuthor = "Airathias";

	public void OnConfigurationLoaded()
	{
		LoggingManager.Log("Mod successfully loaded and configured");
		instance.isInitialized = true;
		instance.Commands.RegisterRCON("reload", OnReloadCommand, CommandFlag.IncludePrefix);
	}

	public void OnReloadCommand(object sender, object[] args)
	{
		instance.Commands.Reset();
		instance.Config.Load();
		LoggingManager.Log("Config reloaded");
	}

	public void Boot()
	{
		try
		{
			ModVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
			Debug.LogWarning((object)("[Harmony] Loaded: Full Range Autoturrets v" + ModVersion + " by Airathias"));
			instance.isLoaded = true;
			instance.Config = new ConfigManager();
			instance.Config.OnConfigLoaded += OnConfigurationLoaded;
			instance.Commands = new CommandManager();
			instance.Config.Load();
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[Harmony] Full Range Autoturrets failed to load: " + ex.Message));
			Debug.LogError((object)ex.StackTrace);
		}
	}

	public static void CheckBootAndInit()
	{
		if (instance == null)
		{
			instance = new Main();
		}
		if (!instance.isLoaded)
		{
			instance.Boot();
		}
	}
}
