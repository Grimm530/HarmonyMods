using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class PlaceMonuments_Process
{
	private static FieldRef<PlaceMonuments, PlaceMonuments.DistanceMode> DistanceDifferentType = AccessTools.FieldRefAccess<PlaceMonuments, PlaceMonuments.DistanceMode>("DistanceDifferentType");

	private static FieldRef<PlaceMonuments, PlaceMonuments.DistanceMode> DistanceSameType = AccessTools.FieldRefAccess<PlaceMonuments, PlaceMonuments.DistanceMode>("DistanceSameType");

	private static FieldRef<PlaceMonuments, int> TargetCount = AccessTools.FieldRefAccess<PlaceMonuments, int>("TargetCount");

	private static FieldRef<PlaceMonuments, int> MinWorldSize = AccessTools.FieldRefAccess<PlaceMonuments, int>("MinWorldSize");

	private static FieldRef<PlaceMonuments, int> MinDistanceDifferentType = AccessTools.FieldRefAccess<PlaceMonuments, int>("MinDistanceDifferentType");

	private static FieldRef<PlaceMonuments, int> MinDistanceSameType = AccessTools.FieldRefAccess<PlaceMonuments, int>("MinDistanceSameType");

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(PlaceMonuments), "Process", (Type[])null, (Type[])null);
	}

	private static bool Prefix(PlaceMonuments __instance)
	{
		//IL_0340: Unknown result type (might be due to invalid IL or missing references)
		//IL_0348: Unknown result type (might be due to invalid IL or missing references)
		//IL_0375: Unknown result type (might be due to invalid IL or missing references)
		//IL_037d: Unknown result type (might be due to invalid IL or missing references)
		//IL_03aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03df: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0414: Unknown result type (might be due to invalid IL or missing references)
		//IL_041c: Unknown result type (might be due to invalid IL or missing references)
		if (ExtConfig.Config.Generator.RemoveTunnelsEntrances && __instance.ResourceFolder == "tunnel-entrance")
		{
			Logging.Generation("Tunnel Entrances off");
			MinWorldSize.Invoke(__instance) = 999999;
		}
		if (ExtConfig.Config.Generator.UniqueEnviroment.ShouldChange && __instance.ResourceFolder.Contains("unique_environment/"))
		{
			switch (__instance.ResourceFolder.Replace("unique_environment/", ""))
			{
			case "oasis":
				Logging.Generation($"UNIQUE ENVIROMENT - Changing generating oasis to {ExtConfig.Config.Generator.UniqueEnviroment.GenerateOasis}");
				if (ExtConfig.Config.Generator.UniqueEnviroment.GenerateOasis)
				{
					MinWorldSize.Invoke(__instance) = 0;
				}
				else
				{
					MinWorldSize.Invoke(__instance) = 999999;
				}
				break;
			case "canyon":
				Logging.Generation($"UNIQUE ENVIROMENT - Changing generating canyon to {ExtConfig.Config.Generator.UniqueEnviroment.GenerateCanyons}");
				if (ExtConfig.Config.Generator.UniqueEnviroment.GenerateCanyons)
				{
					MinWorldSize.Invoke(__instance) = 0;
				}
				else
				{
					MinWorldSize.Invoke(__instance) = 999999;
				}
				break;
			case "lake":
				Logging.Generation($"UNIQUE ENVIROMENT - Changing generating lake to {ExtConfig.Config.Generator.UniqueEnviroment.GenerateLakes}");
				if (ExtConfig.Config.Generator.UniqueEnviroment.GenerateLakes)
				{
					MinWorldSize.Invoke(__instance) = 0;
				}
				else
				{
					MinWorldSize.Invoke(__instance) = 999999;
				}
				break;
			}
		}
		if (!ExtConfig.Config.Monuments.Enabled)
		{
			return true;
		}
		IEnumerable<ExtConfig.Monument> source = ExtConfig.Config.Monuments.monuments.Where((ExtConfig.Monument x) => x.Folder == __instance.ResourceFolder);
		if (!source.Any())
		{
			return true;
		}
		ExtConfig.Monument monument = source.First();
		if (!monument.ShouldChange)
		{
			return true;
		}
		if (!monument.Generate)
		{
			return false;
		}
		DistanceDifferentType.Invoke(__instance) = monument.distanceDifferent;
		DistanceSameType.Invoke(__instance) = monument.distanceSame;
		MinDistanceDifferentType.Invoke(__instance) = monument.MinDistanceDifferentType;
		MinDistanceSameType.Invoke(__instance) = monument.MinDistanceSameType;
		TargetCount.Invoke(__instance) = monument.TargetCount;
		MinWorldSize.Invoke(__instance) = monument.MinWorldSize;
		if (monument.Filter.Enabled)
		{
			__instance.Filter = new SpawnFilter
			{
				BiomeType = (Enum)((monument.Filter.BiomeType.Count == 0) ? (-1) : ((int)(Enum)(object)EnumParser.GetFilterEnum("BiomeType", monument.Filter.BiomeType))),
				SplatType = (Enum)((monument.Filter.BiomeType.Count == 0) ? (-1) : ((int)(Enum)(object)EnumParser.GetFilterEnum("SplatType", monument.Filter.SplatType))),
				TopologyAll = (Enum)((monument.Filter.BiomeType.Count != 0) ? ((int)(Enum)(object)EnumParser.GetFilterEnum("TopologyAll", monument.Filter.TopologyAll)) : 0),
				TopologyAny = (Enum)((monument.Filter.BiomeType.Count == 0) ? (-1) : ((int)(Enum)(object)EnumParser.GetFilterEnum("TopologyAny", monument.Filter.TopologyAny))),
				TopologyNot = (Enum)((monument.Filter.BiomeType.Count != 0) ? ((int)(Enum)(object)EnumParser.GetFilterEnum("TopologyNot", monument.Filter.TopologyNot)) : 0)
			};
		}
		Logging.Generation("Changed instance values for " + monument.Description);
		return true;
	}
}
