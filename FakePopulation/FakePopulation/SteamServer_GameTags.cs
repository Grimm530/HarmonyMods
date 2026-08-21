using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace FakePopulation;

/// <summary>
/// Session / in-game lists read GameTags cp. Rewrite that token from the real
/// active-player count plus BonusPlayers (idempotent if another patch already added).
/// </summary>
[HarmonyPatch]
internal static class SteamServer_GameTags
{
	private static readonly Regex CpToken = new Regex(@"cp\d+", RegexOptions.Compiled);

	static MethodBase TargetMethod()
	{
		var t = AccessTools.TypeByName("Steamworks.SteamServer");
		return t == null ? null : AccessTools.PropertySetter(t, "GameTags");
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	internal static void Prefix(ref string value)
	{
		if (string.IsNullOrEmpty(value))
			return;

		int bonus = FakePopulationConfig.Load().BonusPlayers;
		if (bonus <= 0)
			return;

		int desired = BasePlayer.activePlayerList.Count + bonus;
		value = CpToken.Replace(value, "cp" + desired, 1);
	}
}
