using System;
using HarmonyLib;

namespace FullRangeAutoturrets.Lib.HarmonyPatches;

[HarmonyPatch(typeof(ConsoleSystem), "Run", new Type[]
{
	typeof(ConsoleSystem.Option),
	typeof(string),
	typeof(object[])
})]
internal class ConsoleSystem_BuildCommand
{
	[HarmonyPrepare]
	public static void Prepare()
	{
		Main.CheckBootAndInit();
	}

	[HarmonyPrefix]
	private static bool Prefix(ConsoleSystem.Option options, string strCommand, params object[] args)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			return Main.instance.Commands.Handler(options, strCommand, args);
		}
		catch
		{
		}
		return true;
	}
}
