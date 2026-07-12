using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FullRangeAutoturrets.Lib;
using HarmonyLib;

namespace FullRangeAutoturrets.HarmonyPatches;

[HarmonyPatch(typeof(Bootstrap), "StartServer")]
public class Bootstrap_StartServer_Patch
{
	public static bool Prepare()
	{
		Main.CheckBootAndInit();
		return (bool)Main.instance.Config.Get("Enabled");
	}

	[HarmonyTranspiler]
	public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected O, but got Unknown
		List<CodeInstruction> list = new List<CodeInstruction>(originalInstructions);
		MethodInfo method = typeof(FlameTurretAIBrain).GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic);
		list.InsertRange(0, (IEnumerable<CodeInstruction>)(object)new CodeInstruction[1]
		{
			new CodeInstruction(OpCodes.Call, (object)method)
		});
		return list;
	}
}
