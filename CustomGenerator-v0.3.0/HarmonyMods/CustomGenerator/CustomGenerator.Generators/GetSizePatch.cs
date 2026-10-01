using HarmonyLib;

namespace CustomGenerator.Generators;

[HarmonyPatch(typeof(World), "CanLoadFromDisk")]
public static class GetSizePatch
{
	public static void Postfix(ref bool __result)
	{
		if (ExtConfig.Config.mapSettings.GenerateNewMapEverytime)
		{
			__result = false;
		}
	}
}
