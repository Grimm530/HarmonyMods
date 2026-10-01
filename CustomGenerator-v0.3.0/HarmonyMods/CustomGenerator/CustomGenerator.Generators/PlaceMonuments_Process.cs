using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;
using UnityEngine;

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
		//IL_036c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0374: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_03de: Unknown result type (might be due to invalid IL or missing references)
		//IL_040b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0413: Unknown result type (might be due to invalid IL or missing references)
		//IL_0440: Unknown result type (might be due to invalid IL or missing references)
		//IL_0448: Unknown result type (might be due to invalid IL or missing references)
		if ((ExtConfig.Config.Generator.RemoveTunnelsEntrances || ExtConfig.Config.Generator.RemoveTunnels) && __instance.ResourceFolder == "tunnel-entrance")
		{
			Logging.Generation("Tunnel entrances disabled");
			MinWorldSize.Invoke(__instance) = 999999;
		}
		if (ExtConfig.Config.Generator.UniqueEnviroment.ShouldChange && __instance.ResourceFolder.Contains("unique_environment/"))
		{
			switch (__instance.ResourceFolder.Replace("unique_environment/", ""))
			{
			case "oasis":
				Logging.Generation("Unique environment: oases " + (ExtConfig.Config.Generator.UniqueEnviroment.GenerateOasis ? "on any map size" : "disabled"));
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
				Logging.Generation("Unique environment: canyons " + (ExtConfig.Config.Generator.UniqueEnviroment.GenerateCanyons ? "on any map size" : "disabled"));
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
				Logging.Generation("Unique environment: lakes " + (ExtConfig.Config.Generator.UniqueEnviroment.GenerateLakes ? "on any map size" : "disabled"));
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
				SplatType = (Enum)((monument.Filter.SplatType.Count == 0) ? (-1) : ((int)(Enum)(object)EnumParser.GetFilterEnum("SplatType", monument.Filter.SplatType))),
				TopologyAll = (Enum)((monument.Filter.TopologyAll.Count != 0) ? ((int)(Enum)(object)EnumParser.GetFilterEnum("TopologyAll", monument.Filter.TopologyAll)) : 0),
				TopologyAny = (Enum)((monument.Filter.TopologyAny.Count == 0) ? (-1) : ((int)(Enum)(object)EnumParser.GetFilterEnum("TopologyAny", monument.Filter.TopologyAny))),
				TopologyNot = (Enum)((monument.Filter.TopologyNot.Count != 0) ? ((int)(Enum)(object)EnumParser.GetFilterEnum("TopologyNot", monument.Filter.TopologyNot)) : 0)
			};
		}
		if (!string.IsNullOrEmpty(monument.OverrideFolder))
		{
			Logging.Generation(monument.Description + ": folder '" + __instance.ResourceFolder + "' -> '" + monument.OverrideFolder + "'");
			__instance.ResourceFolder = monument.OverrideFolder;
		}
		if (monument.IgnoreWorldSizeMultiplier)
		{
			__instance.TargetCountWorldSizeMultiplier = AnimationCurve.Constant(0f, 100000f, 1f);
		}
		if (monument.HasPrefabRules)
		{
			Prefab_FindPrefabNames.Active = monument;
		}
		Logging.Generation(monument.Description + ": group settings applied");
		return true;
	}

	private static void Finalizer()
	{
		Prefab_FindPrefabNames.Active = null;
	}
}
