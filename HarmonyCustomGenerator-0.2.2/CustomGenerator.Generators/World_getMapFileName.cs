using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "get_MapFileName")]
public static class World_getMapFileName
{
	public static void Postfix(ref string __result)
	{
		if (ExtConfig.Config.mapSettings.OverrideName)
		{
			string text = string.Format(ExtConfig.Config.mapSettings.MapName, ExtConfig.tempData.mapsize, ExtConfig.tempData.mapseed) + ((!ExtConfig.Config.mapSettings.MapName.EndsWith(".map")) ? ".map" : "");
			Logging.Info("Override map name to " + text);
			__result = text;
		}
	}
}
