using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;
using ProtoBuf;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class PlaceMonuments_Report
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		string[] array = new string[3] { "PlaceMonuments", "PlaceMonumentsRoadside", "PlaceMonumentsRailside" };
		for (int i = 0; i < array.Length; i++)
		{
			Type type = AccessTools.TypeByName(array[i]);
			MethodInfo methodInfo = ((type == null) ? null : AccessTools.Method(type, "Process", (Type[])null, (Type[])null));
			if (methodInfo != null)
			{
				yield return methodInfo;
			}
		}
	}

	[HarmonyPriority(800)]
	private static void Prefix(ProceduralComponent __instance, out int __state)
	{
		__state = (World.Serialization?.world?.prefabs?.Count).GetValueOrDefault();
		GenerationReport.CurrentFolder = (__instance as PlaceMonuments)?.ResourceFolder;
	}

	[HarmonyPriority(800)]
	private static void Postfix(ProceduralComponent __instance, int __state)
	{
		string folder = GenerationReport.CurrentFolder;
		GenerationReport.CurrentFolder = null;
		List<PrefabData> list = World.Serialization?.world?.prefabs;
		if (list != null)
		{
			IEnumerable<string> prefabs = from x in list.Skip(__state)
				select Path.GetFileNameWithoutExtension(StringPool.Get(x.id) ?? x.id.ToString());
			ExtConfig.Monument monument = ((ExtConfig.Config.Monuments.Enabled && folder != null) ? ExtConfig.Config.Monuments.monuments.FirstOrDefault((ExtConfig.Monument x) => x.ShouldChange && x.Folder == folder) : null);
			int target = ((monument == null || monument.TargetCount <= 0 || !monument.IgnoreWorldSizeMultiplier) ? (-1) : (monument.Generate ? monument.TargetCount : 0));
			GenerationReport.MonumentGroup(__instance.Description, folder, target, prefabs);
		}
	}
}
