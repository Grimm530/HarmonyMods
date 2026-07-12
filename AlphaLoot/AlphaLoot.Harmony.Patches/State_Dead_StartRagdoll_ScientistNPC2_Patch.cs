using System;
using HarmonyLib;
using Rust.Ai.Gen2;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(State_Dead), "StartRagdoll")]
public class State_Dead_StartRagdoll_ScientistNPC2_Patch
{
	private static readonly AccessTools.FieldRef<State_Dead, LootContainer.LootSpawnSlot[]> LootSpawnSlotsRef = AccessTools.FieldRefAccess<State_Dead, LootContainer.LootSpawnSlot[]>("LootSpawnSlots");

	[HarmonyPrefix]
	private static void Prefix(State_Dead __instance, ref LootContainer.LootSpawnSlot[] __state)
	{
		__state = null;
		AlphaLootMod instance = AlphaLootMod.Instance;
		ScientistNPC2 scientistNPC = __instance?.Owner as ScientistNPC2;
		if (instance != null && !((Object)(object)scientistNPC == (Object)null) && instance.TryGetNPCProfile(scientistNPC.ShortPrefabName, out var profile) && profile.Enabled)
		{
			__state = LootSpawnSlotsRef(__instance);
			LootSpawnSlotsRef(__instance) = Array.Empty<LootContainer.LootSpawnSlot>();
		}
	}

	[HarmonyPostfix]
	private static void Postfix(State_Dead __instance, LootContainer.LootSpawnSlot[] __state)
	{
		if (__state != null)
		{
			LootSpawnSlotsRef(__instance) = __state;
		}
	}
}
