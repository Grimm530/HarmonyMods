using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CustomGenerator.Custom;

[HarmonyPatch]
internal static class PathGenerators_RunCustomMonuments
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(GenerateRailRing), "Process", (Type[])null, (Type[])null);
		yield return AccessTools.Method(typeof(GenerateRailLayout), "Process", (Type[])null, (Type[])null);
		yield return AccessTools.Method(typeof(GenerateRoadRing), "Process", (Type[])null, (Type[])null);
		yield return AccessTools.Method(typeof(GenerateRoadLayout), "Process", (Type[])null, (Type[])null);
	}

	private static void Prefix(ProceduralComponent __instance, uint seed)
	{
		CustomMonumentPlacer.Run(seed ^ 0x5EED, "fallback: before " + __instance.Description);
	}
}
