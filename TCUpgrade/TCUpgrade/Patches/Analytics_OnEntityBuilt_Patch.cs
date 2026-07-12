using System;
using Facepunch.Rust;
using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(Analytics.Azure), "OnEntityBuilt")]
public static class Analytics_OnEntityBuilt_Patch
{
	private const string LogPrefix = "[TCUpgrade.Patch] ";

	[HarmonyPostfix]
	public static void Postfix(BaseEntity entity, BasePlayer player)
	{
		try
		{
			if ((Object)(object)entity == (Object)null || (Object)(object)player == (Object)null || TCUpgradeMod.Instance == null || !(entity is BuildingPrivlidge buildingPrivlidge))
			{
				return;
			}
			if (!TCUpgradeMod.Instance.HasPermission(player.UserIDString, "TCUpgrade.tcskindeployed"))
			{
				AddAutoLock(player, buildingPrivlidge);
				return;
			}
			TCSkin playerSelectedSkin = TCUpgradeMod.Instance.GetPlayerSelectedSkin(player.userID);
			(string, string, string, int, int) value;
			(string, string, string, int, int) tuple = (TCSkinMeta.Data.TryGetValue(playerSelectedSkin, out value) ? value : TCSkinMeta.Data[TCSkin.Default]);
			if (buildingPrivlidge.ShortPrefabName != tuple.Item1)
			{
				TCUpgradeMod.Instance.TCSkinReplace(buildingPrivlidge, player, playerSelectedSkin);
			}
			else
			{
				AddAutoLock(player, buildingPrivlidge);
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[TCUpgrade.Patch] Analytics_OnEntityBuilt_Patch.Postfix failed: " + ex.Message + "\n" + ex.StackTrace));
		}
	}

	private static void AddAutoLock(BasePlayer player, BuildingPrivlidge tc)
	{
		if ((Object)(object)tc == (Object)null || tc.IsDestroyed || (Object)(object)player == (Object)null || !tc.HasSlot(BaseEntity.Slot.Lock))
		{
			return;
		}
		if (TCUpgradeMod.Instance.HasPermission(player.UserIDString, "TCUpgrade.autocodelock"))
		{
			if (GameManager.server.CreateEntity("assets/prefabs/locks/keypad/lock.code.prefab") as CodeLock is CodeLock codeLock)
			{
				codeLock.OwnerID = player.userID;
				codeLock.code = Random.Range(1000, 9999).ToString();
				codeLock.SetParent(tc, tc.GetSlotAnchorName(BaseEntity.Slot.Lock));
				((Component)codeLock).transform.localPosition = Vector3.zero;
				((Component)codeLock).transform.localRotation = Quaternion.identity;
				codeLock.Spawn();
				tc.SetSlot(BaseEntity.Slot.Lock, codeLock);
				codeLock.SetFlag(BaseEntity.Flags.Locked, b: true);
				codeLock.whitelistPlayers.Add(player.userID);
				player.SendConsoleCommand("chat.add", 2, 0, "<color=#55aaff>[TCUpgrade]</color> " + LangHelper.Lang("AutoCodeLockAdded", codeLock.code));
			}
		}
		else if (TCUpgradeMod.Instance.HasPermission(player.UserIDString, "TCUpgrade.autolock") && GameManager.server.CreateEntity("assets/prefabs/locks/keylock/lock.key.prefab") as KeyLock is KeyLock keyLock)
		{
			keyLock.OwnerID = player.userID;
			keyLock.SetParent(tc, tc.GetSlotAnchorName(BaseEntity.Slot.Lock));
			((Component)keyLock).transform.localPosition = Vector3.zero;
			((Component)keyLock).transform.localRotation = Quaternion.identity;
			keyLock.Spawn();
			tc.SetSlot(BaseEntity.Slot.Lock, keyLock);
			keyLock.SetFlag(BaseEntity.Flags.Locked, b: true);
		}
	}
}
