using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using StackManager.Helpers;

namespace StackManager.Patches;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
public class Bootstrap_StartupShared
{
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
	{
		MethodInfo Target = AccessTools.Method(typeof(ItemManager), "Initialize", (Type[])null, (Type[])null);
		foreach (CodeInstruction Instruction in Instructions)
		{
			if (Instruction.operand as MethodInfo == Target)
			{
				yield return Instruction;
				yield return new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(Stacker), "Initialize", (Type[])null, (Type[])null));
			}
			else
			{
				yield return Instruction;
			}
		}
	}
}
