using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(BradleyAPC), "ServerInit")]
public class BradleyAPC_ServerInit_Patch
{
	[HarmonyPostfix]
	private static void Postfix(BradleyAPC __instance)
	{
		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance != null && instance.BradleyCrates >= 0 && (Object)(object)__instance != (Object)null)
		{
			__instance.maxCratesToSpawn = instance.BradleyCrates;
		}
	}
}
