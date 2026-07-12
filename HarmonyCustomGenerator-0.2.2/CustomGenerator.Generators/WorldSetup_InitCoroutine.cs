using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class WorldSetup_InitCoroutine
{
	private static FieldInfo _monuments = AccessTools.TypeByName("PlaceMonuments").GetField("Monuments", BindingFlags.NonPublic);

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(WorldSetup), "InitCoroutine", (Type[])null, (Type[])null);
	}

	private static bool Prefix(WorldSetup __instance)
	{
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		if (!ExtConfig.tempData.shouldGetMonuments || !ExtConfig.Config.Monuments.Enabled)
		{
			return true;
		}
		PlaceMonuments[] array = ((Component)SingletonComponent<WorldSetup>.Instance).GetComponentsInChildren<ProceduralComponent>(true).OfType<PlaceMonuments>().ToArray();
		Logging.Info($"Founded {array.Length} PlaceMonuments.");
		ExtConfig.Config.Monuments.monuments.Clear();
		PlaceMonuments[] array2 = array;
		foreach (PlaceMonuments placeMonuments in array2)
		{
			ExtConfig.Config.Monuments.monuments.Add(new ExtConfig.Monument
			{
				Description = placeMonuments.Description,
				Folder = placeMonuments.ResourceFolder,
				distanceDifferent = placeMonuments.DistanceDifferentType,
				distanceSame = placeMonuments.DistanceSameType,
				MinDistanceDifferentType = placeMonuments.MinDistanceDifferentType,
				MinDistanceSameType = placeMonuments.MinDistanceSameType,
				TargetCount = placeMonuments.TargetCount,
				MinWorldSize = placeMonuments.MinWorldSize,
				Filter = new ExtConfig.SpawnFilterCfg
				{
					Enabled = true,
					TopologyAll = GetFilterValue<Enum>(placeMonuments.Filter.TopologyAll),
					TopologyAny = GetFilterValue<Enum>(placeMonuments.Filter.TopologyAny),
					TopologyNot = GetFilterValue<Enum>(placeMonuments.Filter.TopologyNot),
					BiomeType = GetFilterValue<Enum>(placeMonuments.Filter.BiomeType),
					SplatType = GetFilterValue<Enum>(placeMonuments.Filter.SplatType)
				},
				Generate = true,
				ShouldChange = false
			});
		}
		ExtConfig.SaveConfig();
		return true;
	}

	private static List<string> GetFilterValue<T>(T enumValue) where T : Enum
	{
		List<string> list = new List<string>();
		foreach (T value in Enum.GetValues(typeof(T)))
		{
			if (Convert.ToInt64(value) != 0L && enumValue.HasFlag(value))
			{
				list.Add(value.ToString());
			}
		}
		return list;
	}
}
