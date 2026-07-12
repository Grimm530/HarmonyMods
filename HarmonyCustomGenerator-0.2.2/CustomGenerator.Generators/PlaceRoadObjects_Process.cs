using System;
using System.Reflection;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class PlaceRoadObjects_Process
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(PlaceRoadObjects), "Process", (Type[])null, (Type[])null);
	}

	private static bool Prefix(PlaceRoadObjects __instance)
	{
		if (!ExtConfig.Config.Generator.Road.ShouldChange)
		{
			return true;
		}
		if (!ExtConfig.Config.Generator.Road.GenerateSideObjects)
		{
			return false;
		}
		return true;
	}
}
