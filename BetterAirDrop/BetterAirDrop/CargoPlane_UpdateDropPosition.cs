using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BetterAirDrop;

[HarmonyPatch(typeof(CargoPlane), "UpdateDropPosition")]
internal class CargoPlane_UpdateDropPosition
{
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Expected O, but got Unknown
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		HarmonyConfig.LoadConfig();
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		list.Find((CodeInstruction instruction) => instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 250f).operand = HarmonyConfig.Config.PlaneAdditionalHeight;
		int num = list.FindIndex((CodeInstruction instruction) => instruction.opcode == OpCodes.Stfld && ((FieldInfo)instruction.operand).Name == "secondsToTake");
		if (num == -1)
		{
			return list;
		}
		list.InsertRange(num - 1, new List<CodeInstruction>
		{
			new CodeInstruction(OpCodes.Ldc_R4, (object)HarmonyConfig.Config.PlaneSpeedMultiplier),
			new CodeInstruction(OpCodes.Mul, (object)null)
		});
		return list;
	}
}
