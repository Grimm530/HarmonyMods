using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CustomGenerator.Patches;

[HarmonyPatch]
internal static class GenerateRiverLayout_Patch
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(GenerateRiverLayout), "Process", (Type[])null, (Type[])null);
	}

	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		return instructions.ToList();
	}
}
