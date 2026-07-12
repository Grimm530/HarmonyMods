using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class GenerateRoadRing_Process
{
	private static FieldRef<GenerateRoadRing, int> MinSize = AccessTools.FieldRefAccess<GenerateRoadRing, int>("MinWorldSize");

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(GenerateRoadRing), "Process", (Type[])null, (Type[])null);
	}

	private static void Prefix(GenerateRoadRing __instance, ref int seed)
	{
		if (ExtConfig.Config.Generator.Road.ShouldChange)
		{
			if (!ExtConfig.Config.Generator.Road.Enabled)
			{
				MinSize.Invoke(__instance) = int.MaxValue;
				Logging.Generation("Road MinWorldSize changed to max! Dont generate!");
			}
			else if (ExtConfig.Config.Generator.Road.GenerateRing)
			{
				MinSize.Invoke(__instance) = 0;
				Logging.Generation("Road MinWorldSize changed to 0!");
			}
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		if (!ExtConfig.Config.Generator.Road.GenerateRing || !ExtConfig.Config.Generator.Road.ShouldChange)
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
