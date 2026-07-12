using System;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class PlaceMonumentsRailside_Process
{
	private static FieldRef<PlaceMonumentsRailside, int> MinSize = AccessTools.FieldRefAccess<PlaceMonumentsRailside, int>("MinWorldSize");

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(PlaceMonumentsRailside), "Process", (Type[])null, (Type[])null);
	}

	private static void Prefix(PlaceMonumentsRailside __instance)
	{
		if (ExtConfig.Config.Generator.Rail.ShouldChange && !ExtConfig.Config.Generator.Rail.GenerateSideMonuments)
		{
			MinSize.Invoke(__instance) = int.MaxValue;
			Logging.Generation("RailMonuments MinWorldSize changed to max!");
		}
	}
}
