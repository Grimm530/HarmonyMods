using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CommunityTab;

internal sealed class CommunityTabConfig
{
	/// <summary>Strip compressed modded/oxide/carbon tags (^z/^o/^y) from Steam GameTags.</summary>
	public bool StripModdedCategoryTags { get; set; } = true;

	/// <summary>
	/// Strip mode tags that outrank "vanilla" in client ServerInfo.ModePriority
	/// (roleplay, pve, creative, ...) so Mode resolves to vanilla.
	/// Does not change gameplay ConVars such as server.pve.
	/// </summary>
	public bool ForceVanillaMode { get; set; } = true;

	private static readonly string ConfigPath = Path.Combine("HarmonyConfig", "CommunityTab.json");
	private static CommunityTabConfig _cached;

	public static CommunityTabConfig Load()
	{
		if (_cached != null)
			return _cached;

		try
		{
			string dir = Path.GetDirectoryName(ConfigPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			if (File.Exists(ConfigPath))
			{
				_cached = JsonConvert.DeserializeObject<CommunityTabConfig>(File.ReadAllText(ConfigPath))
					?? new CommunityTabConfig();
			}
			else
			{
				_cached = new CommunityTabConfig();
				_cached.Save();
				Debug.Log("[CommunityTab] Wrote default config: HarmonyConfig/CommunityTab.json");
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[CommunityTab] Config load failed, using defaults: {ex.Message}");
			_cached = new CommunityTabConfig();
		}

		return _cached;
	}

	public void Save()
	{
		try
		{
			string dir = Path.GetDirectoryName(ConfigPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);
			File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[CommunityTab] Config save failed: {ex.Message}");
		}
	}

	public static void Reload()
	{
		_cached = null;
		Load();
	}
}
