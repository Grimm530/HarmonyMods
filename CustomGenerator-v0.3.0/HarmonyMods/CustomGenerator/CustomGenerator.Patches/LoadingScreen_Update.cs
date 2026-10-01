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
		return AccessTools.Method(typeof(UI_LoadingScreen), "Update", new Type[1] { typeof(string) }, (Type[])null);
	}

	private static void Prefix(ref string strType)
	{
		if ((Object)(object)ExtConfig.tempData.terrainTexturing == (Object)null || strType != "DONE")
		{
			return;
		}
		Logging.Info($"SIZE: {ExtConfig.tempData.mapsize} | SEED: {ExtConfig.tempData.mapseed}");
		if (ExtConfig.Config.Swap.Enabled)
		{
			string text = Path.Combine(World.MapFolderName, World.MapFileName);
			if (File.Exists(text))
			{
				SwapMonument.Initiate(text);
			}
			else
			{
				Logging.Error("Swap: saved map not found at " + text + ", swap skipped");
			}
		}
		MapImage.RenderMap();
		GenerationReport.Write(Path.Combine(World.MapFolderName, World.MapFileName));
		Application.Quit();
	}
}
