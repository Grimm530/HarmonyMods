using System;
using System.Collections.Generic;

namespace CommunityTab;

/// <summary>
/// Shared GameTags rewriting. Client Mode is derived from TagString via ServerInfo.ModePriority
/// (see Rust.Platform.Common ServerInfo.cctor): roleplay/pve/etc. outrank vanilla.
/// Modded browser tab uses compressed ^z (and ^o/^y for Oxide/Carbon).
/// </summary>
internal static class GameTagsUtil
{
	// Matches ServerInfo.ModePriority order above "vanilla".
	private static readonly string[] ModeTagsAboveVanilla =
	{
		"event", "minigame", "battlefield", "builds", "training", "roleplay", "creative",
		"gmhardcore", "gmsoftcore", "gmprimitive", "pvp", "pve",
		"hardcore", "softcore", "primitive"
	};

	// ServerTagCompressor char codes for those modes (+ wipe/difficulty we keep unless listed).
	private static readonly string[] ModeCompressedAboveVanilla =
	{
		"^e", // minigame
		"^i", // battlefield
		"^k", // builds
		"^d", // training
		"^r", // roleplay
		"^c", // creative
		"^h", // hardcore
		"^s", // softcore
		"^u", // primitive
		"^p"  // pve
	};

	internal static string Rewrite(string gameTags, CommunityTabConfig cfg)
	{
		if (string.IsNullOrEmpty(gameTags) || cfg == null)
			return gameTags;

		string s = gameTags;

		if (cfg.StripModdedCategoryTags)
		{
			// Compressed: ^z=modded, ^o=oxide, ^y=carbon
			s = s.Replace("^z", "").Replace("^o", "").Replace("^y", "");
			s = s.Replace(",modded", "").Replace("modded,", "");
		}

		if (cfg.ForceVanillaMode)
		{
			for (int i = 0; i < ModeCompressedAboveVanilla.Length; i++)
				s = s.Replace(ModeCompressedAboveVanilla[i], "");

			s = StripUncompressedSegments(s, ModeTagsAboveVanilla);
		}

		if (cfg.StripModdedCategoryTags)
			s = StripUncompressedSegments(s, new[] { "modded", "oxide", "carbon" });

		while (s.Contains(",,"))
			s = s.Replace(",,", ",");
		return s.Trim(',', ' ');
	}

	internal static string StripUncompressedBrowserTags(string tags)
	{
		return StripUncompressedSegments(tags, new[] { "modded", "oxide", "carbon" });
	}

	internal static string StripUncompressedModeTagsAboveVanilla(string tags)
	{
		return StripUncompressedSegments(tags, ModeTagsAboveVanilla);
	}

	private static string StripUncompressedSegments(string tags, string[] remove)
	{
		if (string.IsNullOrWhiteSpace(tags))
			return tags;

		string[] parts = tags.Split(',');
		var result = new List<string>(parts.Length);
		for (int i = 0; i < parts.Length; i++)
		{
			string s = parts[i].Trim();
			if (string.IsNullOrEmpty(s))
				continue;

			bool drop = false;
			for (int j = 0; j < remove.Length; j++)
			{
				if (string.Equals(s, remove[j], StringComparison.OrdinalIgnoreCase))
				{
					drop = true;
					break;
				}
			}
			if (!drop)
				result.Add(s);
		}
		return string.Join(",", result).Trim(',', ' ');
	}
}
