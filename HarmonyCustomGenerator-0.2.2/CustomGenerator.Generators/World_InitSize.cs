using System;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "InitSize", new Type[] { typeof(uint) })]
internal static class World_InitSize
{
	private static uint _size;

	private static void Prefix(ref uint size)
	{
		if (ExtConfig.Config.mapSettings.OverrideSizes)
		{
			ExtConfig.tempData.mapsize = size;
			_size = size;
			Logging.Generation("Writed size to convars...");
			if (size > 6000 || size < 1000)
			{
				Logging.Generation($"World ({_size}) - Using size bigger or smaller than default, rewriting limits...");
			}
		}
	}
}
