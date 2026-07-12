using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "get_Size")]
public static class World_getSize
{
	public static void Postfix(ref uint __result)
	{
		if (ExtConfig.Config.mapSettings.OverrideSizes)
		{
			if (ExtConfig.tempData.mapsize == 0)
			{
				Logging.Info("map size == 0!");
			}
			else
			{
				__result = ExtConfig.tempData.mapsize;
			}
		}
	}
}
