using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomGenerator.Utility;
using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch]
internal class Prefab_FindPrefabNames
{
	internal static ExtConfig.Monument Active;

	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(Prefab), "FindPrefabNames", (Type[])null, (Type[])null);
	}

	private static void Postfix(string strPrefab, ref string[] __result)
	{
		if (__result != null)
		{
			GenerationReport.PrefabNames(__result.Select(Path.GetFileNameWithoutExtension));
		}
		ExtConfig.Monument active = Active;
		if (active == null || __result == null)
		{
			return;
		}
		List<string> list = new List<string>();
		foreach (IGrouping<string, string> item in from x in __result
			group x by x)
		{
			string name = Path.GetFileNameWithoutExtension(item.Key);
			if ((active.IncludePrefabs.Count <= 0 || active.IncludePrefabs.Any(name.Contains)) && !active.ExcludePrefabs.Any(name.Contains))
			{
				KeyValuePair<string, int> keyValuePair = active.PrefabCopies.FirstOrDefault((KeyValuePair<string, int> x) => name.Contains(x.Key));
				int num = ((keyValuePair.Key != null) ? keyValuePair.Value : item.Count());
				for (int num2 = 0; num2 < num; num2++)
				{
					list.Add(item.Key);
				}
			}
		}
		string text = string.Join(", ", __result.Distinct().Select(Path.GetFileNameWithoutExtension));
		Logging.Generation($"{active.Description}: '{strPrefab}' {__result.Length} -> {list.Count} candidates (available: {text})");
		__result = list.ToArray();
	}
}
