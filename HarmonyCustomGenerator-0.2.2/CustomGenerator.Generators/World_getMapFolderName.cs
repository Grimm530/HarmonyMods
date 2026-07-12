using System.IO;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "get_MapFolderName")]
public static class World_getMapFolderName
{
	private static readonly string FolderName = "maps";

	private static readonly string FolderLocation = Path.GetFullPath(FolderName);

	public static void Postfix(ref string __result)
	{
		if (ExtConfig.Config.mapSettings.OverrideFolder)
		{
			if (!Directory.Exists(FolderName))
			{
				Directory.CreateDirectory(FolderName);
			}
			Logging.Info("Override save folder to " + FolderLocation);
			__result = FolderLocation;
		}
	}
}
