using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "get_Size")]
public static class World_getSize
{
	public static void Postfix(ref uint __result)
	{
		if (ExtConfig.Config.mapSettings.OverrideSizes && ExtConfig.tempData.mapsize != 0)
		{
			__result = ExtConfig.tempData.mapsize;
		}
	}
}
