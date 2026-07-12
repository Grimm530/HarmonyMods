using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(LootFill), "DelayFill")]
public class LootFill_DelayFill_Patch
{
	[HarmonyPrefix]
	private static bool Prefix(LootFill __instance)
	{
		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance == null)
		{
			return true;
		}
		BaseEntity component = ((Component)__instance).GetComponent<BaseEntity>();
		StorageContainer storageContainer = __instance.StorageContainer;
		if ((Object)(object)component == (Object)null || storageContainer?.inventory == null)
		{
			return true;
		}
		string text = AlphaLootMod.ToLootFillProfileName(component, storageContainer);
		if (!instance.TryGetLootProfile(text, out var profile) || !profile.Enabled)
		{
			return true;
		}
		AlphaLootTools.ClearItemContainer(storageContainer.inventory);
		ItemManager.DoRemoves();
		profile.PopulateLoot(storageContainer.inventory);
		AlphaLootConfig config = instance.Config;
		if (config != null && config.DebugLootTable)
		{
			AlphaLootTools.LogLootIfDebug(storageContainer.inventory, "lootfill=" + text, text, config.GlobalMultiplier, profile.LootMultiplier);
		}
		return false;
	}
}
