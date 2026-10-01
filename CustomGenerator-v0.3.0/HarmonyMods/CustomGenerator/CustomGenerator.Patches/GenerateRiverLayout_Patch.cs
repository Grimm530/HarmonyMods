using System;
using System.Collections.Generic;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Patches;

[HarmonyPatch]
internal static class GenerateRiverLayout_Patch
{
	private static FieldRef<TerrainPath, List<PathList>> _rivers = AccessTools.FieldRefAccess<TerrainPath, List<PathList>>("Rivers");

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(GenerateRiverLayout), "Process", (Type[])null, (Type[])null);
	}

	private static void Prefix(out int __state)
	{
		__state = _rivers.Invoke(TerrainMeta.Path).Count;
	}

	private static void Postfix(int __state)
	{
		float riverWidthScale = ExtConfig.Config.Generator.RiverWidthScale;
		if (World.Networked || Mathf.Approximately(riverWidthScale, 1f))
		{
			return;
		}
		if (riverWidthScale <= 0f)
		{
			Logging.Error($"River width scale must be > 0 (got {riverWidthScale}), skipping.");
			return;
		}
		List<PathList> list = _rivers.Invoke(TerrainMeta.Path);
		for (int i = __state; i < list.Count; i++)
		{
			list[i].Width *= riverWidthScale;
		}
		Logging.Generation($"River width scaled x{riverWidthScale} for {list.Count - __state} rivers");
	}
}
