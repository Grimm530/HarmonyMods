using System;
using System.Collections.Generic;
using System.Reflection;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace CommunityTab;

/// <summary>
/// Strips the "modded" tag from server tags before they are sent to Steam so the server
/// appears in the Community tab instead of the Modded tab. Equivalent to oxide.config.json
/// Options.Modded = false when that setting is not respected (e.g. other mods adding "modded").
/// </summary>
[HarmonyPatch(typeof(ServerMgr), "UpdateServerInformation")]
internal static class ServerMgr_UpdateServerInformation
{
	private static FieldInfo _tagsField;
	private static PropertyInfo _gameTagsProperty;

	[HarmonyPrefix]
	internal static void Prefix()
	{
		try
		{
			// ConVar.Server.tags is built from private static _tags. We strip "modded" by
			// setting the backing field so the next UpdateServerInformation build doesn't include it.
			string current = ConVar.Server.tags ?? "";
			string stripped = StripModdedTag(current);
			if (stripped == current)
				return;

			if (_tagsField == null)
			{
				_tagsField = typeof(ConVar.Server).GetField("_tags",
					BindingFlags.NonPublic | BindingFlags.Static);
				if (_tagsField == null)
				{
					Debug.LogWarning("[CommunityTab] Could not find ConVar.Server._tags field.");
					return;
				}
			}

			_tagsField.SetValue(null, stripped);
		}
		catch (Exception ex)
		{
			Debug.LogException(ex);
		}
	}

	/// <summary>
	/// Other Harmony mods (e.g. MixingSpeed, CraftingSpeed) use a Postfix that append ",modded" to
	/// SteamServer.GameTags after the original runs. We run last and strip it from the final string.
	/// </summary>
	[HarmonyPostfix]
	[HarmonyPriority(HarmonyLib.Priority.Last)]
	internal static void Postfix()
	{
		try
		{
			if (_gameTagsProperty == null)
			{
				Type steamServer = AccessTools.TypeByName("Steamworks.SteamServer");
				_gameTagsProperty = steamServer?.GetProperty("GameTags", BindingFlags.Static | BindingFlags.Public);
				if (_gameTagsProperty == null)
				{
					Debug.LogWarning("[CommunityTab] Could not find SteamServer.GameTags.");
					return;
				}
			}

			string gameTags = _gameTagsProperty.GetValue(null) as string;
			if (string.IsNullOrEmpty(gameTags))
				return;

			string stripped = StripModdedFromGameTags(gameTags);
			if (stripped == gameTags)
				return;

			_gameTagsProperty.SetValue(null, stripped);
		}
		catch (Exception ex)
		{
			Debug.LogException(ex);
		}
	}

	/// <summary>Remove ",modded" or "modded," from the compressed GameTags string.</summary>
	private static string StripModdedFromGameTags(string gameTags)
	{
		// Other mods append ",modded" so it appears as ...,modded
		string s = gameTags.Replace(",modded", "").Replace("modded,", "");
		// Clean double commas and trim
		while (s.Contains(",,"))
			s = s.Replace(",,", ",");
		return s.Trim(',', ' ');
	}

	private static string StripModdedTag(string tags)
	{
		if (string.IsNullOrWhiteSpace(tags))
			return tags;
		string[] parts = tags.Split(',');
		var result = new List<string>(parts.Length);
		for (int i = 0; i < parts.Length; i++)
		{
			string s = parts[i].Trim();
			if (string.IsNullOrEmpty(s) || string.Equals(s, "modded", StringComparison.OrdinalIgnoreCase))
				continue;
			result.Add(s);
		}
		return string.Join(",", result).Trim(',', ' ');
	}
}
