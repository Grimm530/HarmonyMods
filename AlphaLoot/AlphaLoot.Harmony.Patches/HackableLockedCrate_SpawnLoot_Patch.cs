using System;
using HarmonyLib;

namespace AlphaLoot.Harmony.Patches;

/// <summary>
/// Matches Oxide AlphaLoot 3.1.50: temporarily detach onItemAddedRemoved during HackableLockedCrate loot spawn
/// so HasBeenLooted is not set while the crate is being populated.
/// </summary>
[HarmonyPatch(typeof(LootContainer), nameof(LootContainer.SpawnLoot))]
public static class HackableLockedCrate_SpawnLoot_Patch
{
	[HarmonyPrefix]
	private static void Prefix(LootContainer __instance)
	{
		if (__instance is not HackableLockedCrate || __instance.inventory == null)
		{
			return;
		}

		__instance.inventory.onItemAddedRemoved = null;
	}

	[HarmonyPostfix]
	private static void Postfix(LootContainer __instance)
	{
		if (__instance is not HackableLockedCrate || __instance.inventory == null)
		{
			return;
		}

		__instance.inventory.onItemAddedRemoved = __instance.OnItemAddedOrRemoved;
	}
}
