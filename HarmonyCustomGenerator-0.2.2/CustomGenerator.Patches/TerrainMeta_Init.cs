using System;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Patches;

[HarmonyPatch]
internal static class TerrainMeta_Init
{
	private static PropertyInfo _terrainPath = AccessTools.TypeByName("TerrainMeta").GetProperty("Path", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

	private static PropertyInfo _terrainTexturing = AccessTools.TypeByName("TerrainMeta").GetProperty("Texturing", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(TerrainMeta), "Init", (Type[])null, (Type[])null);
	}

	private static void Postfix(TerrainMeta __instance)
	{
		ExtConfig.tempData.terrainMeta = __instance;
		ExtConfig.tempData.terrainTexturing = (TerrainTexturing)_terrainTexturing.GetValue(__instance);
		ExtConfig.tempData.terrainPath = (TerrainPath)_terrainPath.GetValue(__instance);
		if ((Object)(object)ExtConfig.tempData.terrainPath == (Object)null || (Object)(object)ExtConfig.tempData.terrainTexturing == (Object)null || (Object)(object)ExtConfig.tempData.terrainMeta == (Object)null)
		{
			Logging.Error("One of components is null!");
		}
		Logging.Info("Saved TerrainTexturing instance!");
	}
}
