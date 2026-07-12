using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Windows;

namespace CommandHistory.Patches;

[HarmonyPatch(typeof(ConsoleInput))]
[HarmonyPatch("Update")]
internal static class ConsoleUpdate
{
	public static void UpHook(ConsoleInput _this)
	{
		string up = OnEnter.GetUp();
		if (up != null)
		{
			_this.inputString = up;
			_this.RedrawInputLine();
		}
	}

	public static void DownHook(ConsoleInput _this)
	{
		string down = OnEnter.GetDown();
		if (down != null)
		{
			_this.inputString = down;
			_this.RedrawInputLine();
		}
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Expected O, but got Unknown
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Expected O, but got Unknown
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected O, but got Unknown
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Expected O, but got Unknown
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Expected O, but got Unknown
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Expected O, but got Unknown
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected O, but got Unknown
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0140: Expected O, but got Unknown
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Expected O, but got Unknown
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Expected O, but got Unknown
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		//IL_019b: Expected O, but got Unknown
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Expected O, but got Unknown
		//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c4: Expected O, but got Unknown
		//IL_01cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Expected O, but got Unknown
		//IL_01f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fd: Expected O, but got Unknown
		//IL_0205: Unknown result type (might be due to invalid IL or missing references)
		//IL_020f: Expected O, but got Unknown
		//IL_0217: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Expected O, but got Unknown
		bool flag = false;
		bool flag2 = false;
		Label label = generator.DefineLabel();
		Label label2 = generator.DefineLabel();
		List<CodeInstruction> list = new List<CodeInstruction>();
		foreach (CodeInstruction instruction in instructions)
		{
			if (flag && !flag2)
			{
				list.Add(new CodeInstruction(OpCodes.Stloc_0, (object)null));
				list.Add(new CodeInstruction(OpCodes.Ldloca, (object)0));
				list.Add(new CodeInstruction(OpCodes.Call, (object)typeof(ConsoleKeyInfo).GetProperty("Key", BindingFlags.Instance | BindingFlags.Public).GetMethod));
				list.Add(new CodeInstruction(OpCodes.Ldc_I4, (object)38));
				list.Add(new CodeInstruction(OpCodes.Sub, (object)null));
				list.Add(new CodeInstruction(OpCodes.Brtrue, (object)label));
				list.Add(new CodeInstruction(OpCodes.Ldarg_0, (object)null));
				list.Add(new CodeInstruction(OpCodes.Call, (object)typeof(ConsoleUpdate).GetMethod("UpHook", BindingFlags.Static | BindingFlags.Public)));
				list.Add(new CodeInstruction(OpCodes.Ret, (object)null));
				list.Add(new CodeInstruction(OpCodes.Nop, (object)null)
				{
					labels = new List<Label> { label }
				});
				list.Add(new CodeInstruction(OpCodes.Ldloca, (object)0));
				list.Add(new CodeInstruction(OpCodes.Call, (object)typeof(ConsoleKeyInfo).GetProperty("Key", BindingFlags.Instance | BindingFlags.Public).GetMethod));
				list.Add(new CodeInstruction(OpCodes.Ldc_I4, (object)40));
				list.Add(new CodeInstruction(OpCodes.Sub, (object)null));
				list.Add(new CodeInstruction(OpCodes.Brtrue, (object)label2));
				list.Add(new CodeInstruction(OpCodes.Ldarg_0, (object)null));
				list.Add(new CodeInstruction(OpCodes.Call, (object)typeof(ConsoleUpdate).GetMethod("DownHook", BindingFlags.Static | BindingFlags.Public)));
				list.Add(new CodeInstruction(OpCodes.Ret, (object)null));
				list.Add(new CodeInstruction(OpCodes.Nop, (object)null)
				{
					labels = new List<Label> { label2 }
				});
				flag2 = true;
			}
			else
			{
				if (instruction.opcode == OpCodes.Call && (MethodInfo)instruction.operand == typeof(Console).GetMethod("ReadKey", BindingFlags.Static | BindingFlags.Public, null, new Type[0], null) && !flag2)
				{
					flag = true;
				}
				list.Add(instruction);
			}
		}
		return list;
	}
}
