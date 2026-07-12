using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace FakePopulation;

/// <summary>
/// Shared transpiler: finds BasePlayer.activePlayerList + get_Count and inserts + bonus.
/// Used by ServerMgr (GameTags), CompanionServer.Info (app/server browser), and Nexus.PingHandler (in-game list).
/// </summary>
internal static class FakePopulationTranspiler
{
	public static IEnumerable<CodeInstruction> AddBonusToActivePlayerCount(
		IEnumerable<CodeInstruction> instructions,
		int bonus,
		out bool found)
	{
		found = false;
		if (bonus <= 0)
			return instructions;

		var codes = new List<CodeInstruction>();
		foreach (var instr in instructions)
		{
			codes.Add(instr);

			if (!found && instr.opcode == OpCodes.Callvirt && instr.operand is MethodBase mb &&
				mb.Name == "get_Count")
			{
				var prev = codes.Count - 2;
				while (prev >= 0 && codes[prev].opcode == OpCodes.Nop)
					prev--;
				if (prev >= 0 && codes[prev].opcode == OpCodes.Ldsfld && codes[prev].operand is FieldInfo ldsfldField)
				{
					if (ldsfldField.DeclaringType == typeof(BasePlayer) && ldsfldField.Name == "activePlayerList")
					{
						codes.Add(new CodeInstruction(OpCodes.Ldc_I4, bonus));
						codes.Add(new CodeInstruction(OpCodes.Add));
						found = true;
					}
				}
			}
		}
		return codes;
	}
}
