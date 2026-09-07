using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace FakePopulation;

/// <summary>
/// Harmony patch to show inflated player count in Steam server browser.
/// Uses: (1) Transpiler to add bonus to cp in GameTags, (2) Postfix to set SteamServer.BotCount
/// for Steam client A2S_INFO display.
/// </summary>
[HarmonyPatch(typeof(ServerMgr), "UpdateServerInformation")]
internal class ServerMgr_UpdateServerInformation
{
	private static int _bonusPlayers = 30;

	public static void SetBonus(int bonus)
	{
		_bonusPlayers = Math.Max(0, Math.Min(bonus, 999));
	}

	[HarmonyTranspiler]
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var config = FakePopulationConfig.Load();
		_bonusPlayers = config.BonusPlayers;
		var result = FakePopulationTranspiler.AddBonusToActivePlayerCount(instructions, _bonusPlayers, out var found);
		if (found)
			Debug.Log($"[FakePopulation] Patched server browser (GameTags cp): +{_bonusPlayers} fake players");
		else
			Debug.LogWarning("[FakePopulation] Transpiler could not find injection point in UpdateServerInformation - game may have been updated. Using BotCount fallback only.");
		return result;
	}

	[HarmonyPostfix]
	public static void Postfix()
	{
		var bonus = FakePopulationConfig.Load().BonusPlayers;
		if (bonus <= 0) return;

		// Steam client's A2S_INFO may use BotCount; set it as fallback for clients that ignore GameTags cp
		try
		{
			var steamAsm = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name?.StartsWith("Facepunch.Steamworks") == true);
			var steamServerType = steamAsm?.GetType("Facepunch.Steamworks.SteamServer");
			var botCountProp = steamServerType?.GetProperty("BotCount", BindingFlags.Public | BindingFlags.Static);
			if (botCountProp != null)
				botCountProp.SetValue(null, bonus);
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[FakePopulation] Could not set SteamServer.BotCount: {ex.Message}");
		}
	}
}
