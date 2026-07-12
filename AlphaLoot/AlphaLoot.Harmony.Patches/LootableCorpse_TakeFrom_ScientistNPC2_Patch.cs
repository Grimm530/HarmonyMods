using System;
using HarmonyLib;
using Rust.Ai.Gen2;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(LootableCorpse), "TakeFrom", new Type[]
{
	typeof(BaseEntity),
	typeof(ItemContainer)
})]
public class LootableCorpse_TakeFrom_ScientistNPC2_Patch
{
	[HarmonyPostfix]
	private static void Postfix(LootableCorpse __instance, BaseEntity fromEntity)
	{
		AlphaLootMod instance = AlphaLootMod.Instance;
		ScientistNPC2 scientistNPC = fromEntity as ScientistNPC2;
		if (instance != null && !((Object)(object)scientistNPC == (Object)null) && instance.TryGetNPCProfile(scientistNPC.ShortPrefabName, out var profile) && profile.Enabled)
		{
			instance.PopulateCorpseLoot(scientistNPC, __instance);
		}
	}
}
