using System;
using System.IO;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Patches;

[HarmonyPatch]
internal static class LoadingScreen_Update
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(LoadingScreen), "Update", new Type[1] { typeof(string) }, (Type[])null);
	}

	private static void Prefix(ref string strType)
	{
		if (!((Object)(object)ExtConfig.tempData.terrainTexturing == (Object)null) && !(strType != "DONE"))
		{
			Logging.Info($"SIZE: {ExtConfig.tempData.mapsize} | SEED: {ExtConfig.tempData.mapseed}");
			if (ExtConfig.Config.Swap.Enabled)
			{
				SwapMonument.Initiate(Path.GetFullPath("maps") + "\\" + string.Format(ExtConfig.Config.mapSettings.MapName, ExtConfig.tempData.mapsize, ExtConfig.tempData.mapseed) + ((!ExtConfig.Config.mapSettings.MapName.EndsWith(".map")) ? ".map" : ""));
			}
			MapImage.RenderMap(0.75f, 150);
			Application.Quit();
		}
	}
}
