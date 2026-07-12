using System;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class PlaceDecorUniform_Process
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(PlaceDecorUniform), "Process", (Type[])null, (Type[])null);
	}

	private static bool Prefix(PlaceDecorUniform __instance)
	{
		if (!ExtConfig.Config.Generator.RemoveCarWrecks)
		{
			return true;
		}
		if (__instance.Description == "Roadside Wrecks")
		{
			Logging.Generation("Removing wrecks.");
			return false;
		}
		return true;
	}
}
