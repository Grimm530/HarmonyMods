using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace TCUpgrade;

public class TCUpgradeData
{
	public string Version = "1.6.0";

	public Dictionary<string, HashSet<ulong>> CustomWallpapers = new Dictionary<string, HashSet<ulong>>(StringComparer.OrdinalIgnoreCase);

	private static string DataPath => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "HarmonyConfig", "TCUpgrade", "data.json");

	public static TCUpgradeData Load()
	{
		try
		{
			string dataPath = DataPath;
			string directoryName = Path.GetDirectoryName(dataPath);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			if (File.Exists(dataPath))
			{
				TCUpgradeData tCUpgradeData = JsonConvert.DeserializeObject<TCUpgradeData>(File.ReadAllText(dataPath));
				if (tCUpgradeData == null)
				{
					tCUpgradeData = new TCUpgradeData();
				}
				TCUpgradeData tCUpgradeData2 = tCUpgradeData;
				if (tCUpgradeData2.CustomWallpapers == null)
				{
					tCUpgradeData2.CustomWallpapers = new Dictionary<string, HashSet<ulong>>(StringComparer.OrdinalIgnoreCase);
				}
				return tCUpgradeData;
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[TCUpgrade] Data load error: " + ex.Message));
		}
		return new TCUpgradeData();
	}

	public void Save()
	{
		try
		{
			string dataPath = DataPath;
			string directoryName = Path.GetDirectoryName(dataPath);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(dataPath, JsonConvert.SerializeObject((object)this, (Formatting)1));
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[TCUpgrade] Data save error: " + ex.Message));
		}
	}
}
