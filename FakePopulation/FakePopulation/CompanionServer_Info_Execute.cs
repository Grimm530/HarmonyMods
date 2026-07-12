using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FakePopulation;

/// <summary>
/// Inflates player count in Companion Server / app server info (used by some server browser sources).
/// </summary>
[HarmonyPatch]
internal class CompanionServer_Info_Execute
{
	static MethodBase TargetMethod()
	{
		var t = AccessTools.TypeByName("CompanionServer.Handlers.Info");
		return t != null ? AccessTools.Method(t, "Execute") : null;
	}

	[HarmonyTranspiler]
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var bonus = FakePopulationConfig.Load().BonusPlayers;
		var result = FakePopulationTranspiler.AddBonusToActivePlayerCount(instructions, bonus, out var found);
		if (found)
			Debug.Log($"[FakePopulation] Patched CompanionServer.Info (app/browser): +{bonus} fake players");
		return result;
	}
}
