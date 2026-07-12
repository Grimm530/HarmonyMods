using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;

namespace CustomGenerator.Utility;

internal static class MapImage
{
	private static Dictionary<string, string> RequirementResources = new Dictionary<string, string>
	{
		{ "PermanentMarker.ttf", "https://raw.githubusercontent.com/hammzat/HarmonyCustomGenerator/main/Resources/PermanentMarker.ttf" },
		{ "dinpro.otf", "https://raw.githubusercontent.com/hammzat/HarmonyCustomGenerator/main/Resources/dinpro.otf" },
		{ "dinprobold.otf", "https://raw.githubusercontent.com/hammzat/HarmonyCustomGenerator/main/Resources/dinprobold.otf" }
	};

	private static void CheckResources()
	{
		if (!Directory.Exists("mapimages"))
		{
			Directory.CreateDirectory("mapimages");
		}
		if (!Directory.Exists("mapimages/resources"))
		{
			Directory.CreateDirectory("mapimages/resources");
		}
		string path = "mapimages/resources";
		foreach (KeyValuePair<string, string> requirementResource in RequirementResources)
		{
			if (File.Exists(Path.Combine(path, requirementResource.Key)))
			{
				continue;
			}
			using WebClient webClient = new WebClient();
			Logging.Info("DEPS - Downloading `" + requirementResource.Key + "`...");
			try
			{
				webClient.DownloadFile(requirementResource.Value, Path.Combine(path, requirementResource.Key));
			}
			catch (Exception ex)
			{
				Logging.Error("DEPS - Error whilst downloading: " + ex.Message + " \nTry moving file from the `Resources` repository folder to the `mapimages/resources/`");
			}
		}
	}

	public static void RenderMap(float scale = 0.5f, int oceanMargin = 500)
	{
		CheckResources();
		int imageWidth;
		int imageHeight;
		Color background;
		byte[] array = MapImageRender.Render(out imageWidth, out imageHeight, out background, scale, lossy: false, transparent: false, 350);
		if (array == null)
		{
			Logging.Error("MapImageGenerator returned null!");
			return;
		}
		string text = string.Format(ExtConfig.Config.mapSettings.MapName, ExtConfig.tempData.mapsize, ExtConfig.tempData.mapseed).Replace(".map", "");
		File.WriteAllBytes(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "mapimages/" + text + ".png")), array);
		Logging.Info("Generated Map image: /mapimages/");
		Logging.Info(string.Format("Map saved to {0}", ExtConfig.Config.mapSettings.OverrideFolder ? "/maps/" : "original map folder"));
	}
}
