using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MixingSpeed;

[HarmonyPatch(typeof(MixingTable), "StartMixing")]
internal class MixingTable_StartMixing
{
	/// <summary>
	/// Transpiler modifies the RemainingMixTime assignment. Insert at index of the call (not num-1)
	/// so we divide the final value on stack - more robust against IL reordering.
	/// </summary>
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		int callIndex = list.FindIndex((CodeInstruction instruction) => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo mi && mi.Name == "set_RemainingMixTime");
		if (callIndex == -1)
		{
			return list;
		}
		HarmonyConfig.LoadConfig();
		if (HarmonyConfig.Config.InstantMix)
		{
			// Replace value passed to setter with 0. Stack before call: [this, value]. We need [this, 0].
			// Insert Ldc_R4 0 and Div(0/1) would give 0 - but we need to clear the value. Simpler: replace
			// the instruction that pushes the final value with Ldc_R4 0. Very IL-dependent; add bounds check.
			if (callIndex >= 7)
			{
				list[callIndex - 1].opcode = OpCodes.Ldc_R4;
				list[callIndex - 1].operand = 0f;
				list.RemoveRange(callIndex - 7, 6);
			}
		}
		else if (HarmonyConfig.Config.MixingSpeedMultiplier > 0f)
		{
			// Insert before the call: divide the value on stack by multiplier. Stack: [this, value] -> [this, value/mult]
			list.InsertRange(callIndex, new List<CodeInstruction>
			{
				new CodeInstruction(OpCodes.Ldc_R4, (object)HarmonyConfig.Config.MixingSpeedMultiplier),
				new CodeInstruction(OpCodes.Div, (object)null)
			});
		}
		return list;
	}
}
