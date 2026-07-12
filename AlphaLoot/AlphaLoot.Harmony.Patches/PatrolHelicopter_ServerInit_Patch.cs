using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(PatrolHelicopter), "ServerInit")]
public class PatrolHelicopter_ServerInit_Patch
{
	[HarmonyPostfix]
	private static void Postfix(PatrolHelicopter __instance)
	{
		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance != null && instance.HelicopterCrates >= 0 && (Object)(object)__instance != (Object)null)
		{
			__instance.maxCratesToSpawn = instance.HelicopterCrates;
		}
	}
}
