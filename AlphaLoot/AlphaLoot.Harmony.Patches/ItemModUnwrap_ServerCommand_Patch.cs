using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(ItemModUnwrap), "ServerCommand")]
public class ItemModUnwrap_ServerCommand_Patch
{
	[HarmonyPrefix]
	private static bool Prefix(ItemModUnwrap __instance, Item item, string command, BasePlayer player)
	{
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		if (command != "unwrap" || (item != null && item.amount <= 0))
		{
			return true;
		}
		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance == null)
		{
			return true;
		}
		if (!instance.TryGetUnwrapProfile(item.info.shortname, out var profile) || !profile.Enabled)
		{
			return true;
		}
		int num = Random.Range(__instance.minTries, __instance.maxTries + 1);
		for (int i = 0; i < num; i++)
		{
			profile.PopulateLoot(player.inventory.containerMain);
		}
		AlphaLootConfig config = instance.Config;
		if (config != null && config.DebugLootTable)
		{
			Debug.Log((object)$"[AlphaLoot Debug] unwrap | item={item.info.shortname} | attempts={num} | profile={item.info.shortname} | multiplier={config.GlobalMultiplier:F1}x global × {profile.LootMultiplier:F1}x profile");
		}
		item.UseItem();
		if (__instance.successEffect.isValid)
		{
			Effect.server.Run(__instance.successEffect.resourcePath, player.eyes.position);
		}
		return false;
	}
}
