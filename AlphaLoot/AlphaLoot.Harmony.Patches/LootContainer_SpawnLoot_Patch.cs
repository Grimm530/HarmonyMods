using System;
using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(LootContainer), "SpawnLoot")]
public class LootContainer_SpawnLoot_Patch
{
	[HarmonyPrefix]
	private static bool Prefix(LootContainer __instance)
	{
		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance == null)
		{
			return true;
		}
		AlphaLootConfig config = instance.Config;
		bool flag = __instance is SupplyDrop;
		if (flag && config != null && !config.OverrideFancyDrop)
		{
			if (config.DebugSupplyDrops)
			{
				Debug.Log((object)"[AlphaLoot Debug] SupplyDrop SKIPPED - OverrideFancyDrop is false. Set to true to use AlphaLoot supply_drop table.");
			}
			return true;
		}
		if (__instance?.inventory == null)
		{
			if (config != null && config.DebugLootTable)
			{
				Debug.Log((object)("[AlphaLoot Debug] SKIPPED (null inventory) container=" + (__instance?.ShortPrefabName ?? "?") + " | vanilla will run"));
			}
			else
			{
				Debug.Log((object)"CONTACT DEVELOPERS! LootContainer::SpawnLoot has null inventory!!!");
			}
			return true;
		}
		string text = AlphaLootMod.ToProfileName(__instance);
		if (!instance.TryGetLootProfile(text, out var profile))
		{
			if ((flag && config != null && config.DebugSupplyDrops) || (config != null && config.DebugLootTable))
			{
				Debug.Log((object)("[AlphaLoot Debug] SKIPPED (no profile) container=" + (__instance.ShortPrefabName ?? "?") + " | lookup name=" + text + " | vanilla loot will run"));
			}
			return true;
		}
		if (!profile.Enabled)
		{
			if (config != null && config.DebugLootTable)
			{
				Debug.Log((object)("[AlphaLoot Debug] SKIPPED (profile disabled) container=" + text));
			}
			return true;
		}
		AlphaLootTools.ClearItemContainer(__instance.inventory);
		ItemManager.DoRemoves();
		try
		{
			instance.PopulateLootContainer(__instance, profile);
		}
		catch (Exception ex)
		{
			Debug.LogError((object)($"[AlphaLoot.Harmony] Failed to populate {text}: {ex.Message}. Falling back to vanilla loot."));
			return true;
		}
		if (config != null && ((config.DebugSupplyDrops && __instance is SupplyDrop) || config.DebugLootTable))
		{
			float globalMultiplier = config.GlobalMultiplier;
			float profileMult = profile?.LootMultiplier ?? 1f;
			string text2 = __instance.ShortPrefabName ?? text;
			AlphaLootTools.LogLootIfDebug(__instance.inventory, "container=" + text2, text, globalMultiplier, profileMult);
		}
		return false;
	}
}
