using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FakePopulation;

/// <summary>
/// GameTags cp (loading screen + Session) is rewritten by SteamServer_GameTags.
/// Play Community uses Facepunch's trusted Steam player-count snapshot and cannot
/// be changed from GameTags / A2S bots.
/// </summary>
[HarmonyPatch(typeof(ServerMgr), "UpdateServerInformation")]
internal class ServerMgr_UpdateServerInformation
{
	[HarmonyTranspiler]
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var config = FakePopulationConfig.Load();
		var result = FakePopulationTranspiler.AddBonusToActivePlayerCount(instructions, config.BonusPlayers, out var found);
		if (found)
			Debug.Log($"[FakePopulation] Patched UpdateServerInformation GameTags cp: +{config.BonusPlayers}");
		else
			Debug.LogWarning("[FakePopulation] Transpiler missed UpdateServerInformation; GameTags prefix still rewrites cp.");
		return result;
	}
}
