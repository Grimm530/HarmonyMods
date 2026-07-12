using System;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "InitSeed", new Type[] { typeof(uint) })]
internal static class World_InitSeed
{
	private static void Prefix(ref uint seed)
	{
		ExtConfig.tempData.mapseed = seed;
		Logging.Generation("Writed seed to convars...");
	}
}
