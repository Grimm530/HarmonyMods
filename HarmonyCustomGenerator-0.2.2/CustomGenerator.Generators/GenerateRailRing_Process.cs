using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class GenerateRailRing_Process
{
	private static FieldRef<GenerateRailRing, int> MinSize = AccessTools.FieldRefAccess<GenerateRailRing, int>("MinWorldSize");

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(GenerateRailRing), "Process", (Type[])null, (Type[])null);
	}

	private static void Prefix(GenerateRailRing __instance, ref int seed)
	{
		if (ExtConfig.Config.Generator.Rail.ShouldChange)
		{
			if (!ExtConfig.Config.Generator.Rail.Enabled)
			{
				MinSize.Invoke(__instance) = int.MaxValue;
				Logging.Generation("RailRing MinWorldSize changed to max!");
			}
			else if (ExtConfig.Config.Generator.Rail.GenerateRing)
			{
				MinSize.Invoke(__instance) = 0;
				Logging.Generation("RailRing MinWorldSize changed to 0!");
			}
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		if (!ExtConfig.Config.Generator.Rail.GenerateRing || !ExtConfig.Config.Generator.Rail.ShouldChange)
		{
			return list;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].opcode == OpCodes.Ldc_I4 && ulong.TryParse(list[i].operand.ToString(), out var result) && result == 5000)
			{
				list[i].operand = 0;
				break;
			}
		}
		return list;
	}
}
