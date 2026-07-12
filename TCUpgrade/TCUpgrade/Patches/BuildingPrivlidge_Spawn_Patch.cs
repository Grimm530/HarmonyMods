using System;
using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(StorageContainer), "ServerInit")]
public static class BuildingPrivlidge_Spawn_Patch
{
	private const string LogPrefix = "[TCUpgrade.Patch] ";

	[HarmonyPostfix]
	public static void Postfix(StorageContainer __instance)
	{
		try
		{
			if ((Object)(object)__instance == (Object)null || !(__instance is BuildingPrivlidge buildingPrivlidge))
			{
				return;
			}
			TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
			if (config != null && config.Debug)
			{
				Transform transform = ((Component)buildingPrivlidge).transform;
				Debug.Log((object)string.Format("{0}BuildingPrivlidge_Spawn_Patch.Postfix: TC at {1}, skinID={2}", "[TCUpgrade.Patch] ", (transform != null) ? new Vector3?(transform.position) : ((Vector3?)null), buildingPrivlidge.skinID));
			}
			if (buildingPrivlidge.skinID == 0L)
			{
				if (TCUpgradeMod.Instance == null)
				{
					Debug.LogWarning((object)"[TCUpgrade.Patch] BuildingPrivlidge_Spawn_Patch: TCUpgradeMod.Instance is null, skipping UpdateBlockedItems");
				}
				else
				{
					TCUpgradeMod.Instance.UpdateBlockedItems(buildingPrivlidge);
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[TCUpgrade.Patch] BuildingPrivlidge_Spawn_Patch.Postfix failed: " + ex.Message + "\n" + ex.StackTrace));
		}
	}
}
