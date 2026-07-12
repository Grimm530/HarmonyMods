using System;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class PlaceMonumentsRoadside_Process
{
	private static FieldRef<PlaceMonumentsRoadside, int> MinSize = AccessTools.FieldRefAccess<PlaceMonumentsRoadside, int>("MinWorldSize");

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(PlaceMonumentsRoadside), "Process", (Type[])null, (Type[])null);
	}

	private static void Prefix(PlaceMonumentsRoadside __instance, ref int seed)
	{
		if (ExtConfig.Config.Generator.Road.ShouldChange && !ExtConfig.Config.Generator.Road.GenerateSideMonuments)
		{
			MinSize.Invoke(__instance) = 99999;
			Logging.Generation("RoadMonuments MinWorldSize changed to 99999!");
		}
	}
}
