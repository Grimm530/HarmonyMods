using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FullRangeAutoturrets.Lib;
using FullRangeAutoturrets.Lib.Logging;
using HarmonyLib;

namespace FullRangeAutoturrets.HarmonyPatches;

[HarmonyPatch(typeof(FlameTurret), "CheckTrigger")]
public class FlameTurret_CheckTrigger_Patch
{
	public static bool Prepare(MethodBase original)
	{
		Main.CheckBootAndInit();
		return (bool)Main.instance.Config.Get("Enabled") && (bool)Main.instance.Config.Get("FlameTurrets.Enabled");
	}

	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalInstructions)
	{
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected O, but got Unknown
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Expected O, but got Unknown
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Expected O, but got Unknown
		List<CodeInstruction> list = new List<CodeInstruction>(originalInstructions);
		try
		{
			int num = list.FindIndex((CodeInstruction x) => x.opcode == OpCodes.Stloc_2);
			if (num != -1 && list[num - 1].opcode == OpCodes.Ldc_I4_0)
			{
				FieldInfo field = typeof(SingletonComponent<FlameTurretAIBrain>).GetField("Instance", BindingFlags.Static | BindingFlags.Public);
				MethodInfo method = typeof(FlameTurretAIBrain).GetMethod("EvalTargetsInRange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				list[num - 1].opcode = OpCodes.Nop;
				list.InsertRange(num - 1, (IEnumerable<CodeInstruction>)(object)new CodeInstruction[3]
				{
					new CodeInstruction(OpCodes.Ldsfld, (object)field),
					new CodeInstruction(OpCodes.Ldarg_0, (object)null),
					new CodeInstruction(OpCodes.Callvirt, (object)method)
				});
			}
		}
		catch (Exception ex)
		{
			LoggingManager.Log("Unable to patch FlameTurret detection range: " + ex.Message);
		}
		return list;
	}
}
