using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CommunityTab;

/// <summary>
/// Intercept every Steam GameTags write (same choke point FakePopulation targets for cp).
/// Prefix mutates the value before Facepunch.Steamworks pushes it to Steam.
/// </summary>
[HarmonyPatch]
internal static class SteamServer_GameTags
{
	private static bool _logged;

	static MethodBase TargetMethod()
	{
		var t = AccessTools.TypeByName("Steamworks.SteamServer");
		return t == null ? null : AccessTools.PropertySetter(t, "GameTags");
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	internal static void Prefix(ref string value)
	{
		if (string.IsNullOrEmpty(value))
			return;

		var cfg = CommunityTabConfig.Load();
		string rewritten = GameTagsUtil.Rewrite(value, cfg);
		if (rewritten == value)
			return;

		value = rewritten;

		if (!_logged)
		{
			_logged = true;
			Debug.Log($"[CommunityTab] Rewrote SteamServer.GameTags (ForceVanillaMode={cfg.ForceVanillaMode}, StripModded={cfg.StripModdedCategoryTags}): {value}");
		}
	}
}
