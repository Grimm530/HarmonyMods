using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(StorageContainer), "PlayerStoppedLooting")]
public static class StorageContainer_PlayerStoppedLooting_Patch
{
	[HarmonyPostfix]
	public static void Postfix(StorageContainer __instance, BasePlayer player)
	{
		if (!((Object)(object)player == (Object)null) && __instance is BuildingPrivlidge && TCUpgradeMod.Instance != null)
		{
			TCUpgradeMod.Instance.OnLootEnded(player);
		}
	}
}
