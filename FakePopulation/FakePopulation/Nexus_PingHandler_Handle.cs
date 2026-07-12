using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FakePopulation;

/// <summary>
/// Inflates player count in Nexus ping response (in-game server list uses this).
/// </summary>
[HarmonyPatch]
internal class Nexus_PingHandler_Handle
{
	static MethodBase TargetMethod()
	{
		var t = AccessTools.TypeByName("Rust.Nexus.Handlers.PingHandler");
		return t != null ? AccessTools.Method(t, "Handle") : null;
	}

	[HarmonyTranspiler]
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var bonus = FakePopulationConfig.Load().BonusPlayers;
		var result = FakePopulationTranspiler.AddBonusToActivePlayerCount(instructions, bonus, out var found);
		if (found)
			Debug.Log($"[FakePopulation] Patched Nexus.PingHandler (in-game list): +{bonus} fake players");
		return result;
	}
}
