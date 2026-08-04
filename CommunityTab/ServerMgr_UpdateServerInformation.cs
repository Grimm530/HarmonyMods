using System;
using System.Reflection;
using ConVar;
using HarmonyLib;
using UnityEngine;

namespace CommunityTab;

/// <summary>
/// Also clears ConVar.Server._tags before UpdateServerInformation builds the tag string.
/// Final enforcement is SteamServer_GameTags (set_GameTags Prefix).
/// </summary>
[HarmonyPatch(typeof(ServerMgr), "UpdateServerInformation")]
internal static class ServerMgr_UpdateServerInformation
{
	private static FieldInfo _tagsField;

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	internal static void Prefix()
	{
		try
		{
			var cfg = CommunityTabConfig.Load();
			string current = ConVar.Server.tags ?? "";
			string stripped = current;

			if (cfg.StripModdedCategoryTags)
				stripped = GameTagsUtil.StripUncompressedBrowserTags(stripped);

			// Drop uncompressed mode tags from ConVar tags; TruePVE still injects ",pve" via
			// server.pve during UpdateServerInformation — SteamServer_GameTags strips final ^p.
			if (cfg.ForceVanillaMode)
				stripped = GameTagsUtil.StripUncompressedModeTagsAboveVanilla(stripped);

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
}
