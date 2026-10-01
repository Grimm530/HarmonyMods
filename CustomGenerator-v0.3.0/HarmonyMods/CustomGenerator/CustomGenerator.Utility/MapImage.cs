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
		string text = Paths.Get("mapimages", "resources");
		if (!Directory.Exists(text))
		{
			Directory.CreateDirectory(text);
		}
		foreach (KeyValuePair<string, string> requirementResource in RequirementResources)
		{
			if (File.Exists(Path.Combine(text, requirementResource.Key)))
			{
				continue;
			}
			using WebClient webClient = new WebClient();
			Logging.Info("Map image: downloading font " + requirementResource.Key + "...");
			try
			{
				webClient.DownloadFile(requirementResource.Value, Path.Combine(text, requirementResource.Key));
			}
			catch (Exception ex)
			{
				Logging.Error("Map image: can't download " + requirementResource.Key + ": " + ex.Message + ". Copy the fonts from the release archive (mapimages/resources/) to " + text);
			}
		}
	}

	public static void RenderMap()
	{
		ExtConfig.MapImageSettings mapImage = ExtConfig.Config.MapImage;
		if (!mapImage.Enabled)
		{
			Logging.Info("Map image disabled in the config");
			return;
		}
		CheckResources();
		int imageWidth;
		int imageHeight;
		Color background;
		byte[] array = MapImageRender.Render(out imageWidth, out imageHeight, out background, mapImage.Scale, lossy: false, transparent: false, mapImage.OceanMargin);
		if (array == null)
		{
			Logging.Error("Map image: the terrain isn't ready, image skipped");
			return;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(World.MapFileName);
		string text = Paths.Get("mapimages", fileNameWithoutExtension + ".png");
		File.WriteAllBytes(text, array);
		Logging.Info("Map image saved to " + text);
		GenerationReport.Image(text);
	}
}
