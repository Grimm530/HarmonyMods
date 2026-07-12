using HarmonyLib;
using UnityEngine;

namespace BagCooldowns;

[HarmonyPatch(typeof(BaseNetworkable), "Spawn")]
internal class BaseNetworkable_Spawn
{
	[HarmonyPostfix]
	private static void Postfix(BaseNetworkable __instance)
	{
		if (__instance == null || __instance is not SleepingBag sleepingBag)
			return;

		// Skip StaticRespawnArea - it uses its own unlock logic
		if (sleepingBag is StaticRespawnArea)
			return;

		HarmonyMethods.SetSecondsBetweenReUses(sleepingBag);

		// Only set unlock time for newly spawned bags, not when loading from save (preserves cooldowns across restarts)
		if (!Rust.Application.isLoadingSave)
			HarmonyMethods.SetUnlockTime(sleepingBag);
	}
}
