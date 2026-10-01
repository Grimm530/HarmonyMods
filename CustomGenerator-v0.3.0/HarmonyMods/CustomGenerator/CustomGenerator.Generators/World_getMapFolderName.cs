using System.IO;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "get_MapFolderName")]
public static class World_getMapFolderName
{
	private static readonly string FolderLocation = Paths.Get("maps");

	public static void Postfix(ref string __result)
	{
		if (ExtConfig.Config.mapSettings.OverrideFolder || Paths.IsWorkspace)
		{
			if (!Directory.Exists(FolderLocation))
			{
				Directory.CreateDirectory(FolderLocation);
			}
			Logging.Info("Override save folder to " + FolderLocation);
			__result = FolderLocation;
		}
	}
}
