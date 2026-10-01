using System;
using System.Reflection;
using HarmonyLib;

namespace CustomGenerator.Custom;

[HarmonyPatch]
internal static class PlaceMonuments_RunCustomMonuments
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(PlaceMonuments), "Process", (Type[])null, (Type[])null);
	}

	private static void Postfix(PlaceMonuments __instance, uint seed)
	{
		if (__instance.Description == "Main Monuments")
		{
			CustomMonumentPlacer.Run(seed ^ 0x5EED, __instance.Description);
		}
	}
}
