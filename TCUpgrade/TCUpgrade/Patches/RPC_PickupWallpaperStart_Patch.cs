using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(BuildingBlock), "RPC_PickupWallpaperStart")]
public static class RPC_PickupWallpaperStart_Patch
{
	private static bool InvokeBool(object instance, string methodName, object arg)
	{
		Type type = instance?.GetType();
		if (type == null)
		{
			return false;
		}
		MethodInfo methodInfo = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { arg?.GetType() ?? typeof(object) }, null) ?? type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
		if (methodInfo != null)
		{
			return (bool)(methodInfo.Invoke(instance, new object[1] { arg }) ?? ((object)false));
		}
		return false;
	}

	[HarmonyPrefix]
	public static bool Prefix(BuildingBlock __instance, BaseEntity.RPCMessage msg)
	{
		try
		{
			if ((Object)(object)msg.player == (Object)null)
			{
				return true;
			}
			if (!msg.player.CanInteract())
			{
				return false;
			}
			if (!InvokeBool(__instance, "ShouldDisplayPickupOption", msg.player))
			{
				return false;
			}
			if (!InvokeBool(__instance, "CanCompletePickup", msg.player))
			{
				return false;
			}
			bool flag = msg.read.Bool();
			if (!__instance.HasWallpaper((!flag) ? 1 : 0))
			{
				return false;
			}
			if (TCUpgradeMod.Instance != null && TCUpgradeMod.Instance.HasPermission(msg.player.UserIDString, "TCUpgrade.wallpaper.nocost"))
			{
				ItemAmount placementPrice = WallpaperPlanner.Settings.PlacementPrice;
				if ((Object)(object)placementPrice?.itemDef != (Object)null && placementPrice.amount > 0f)
				{
					Item item = ItemManager.Create(placementPrice.itemDef, (int)placementPrice.amount, 0uL);
					msg.player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
				}
			}
			__instance.RemoveWallpaper((!flag) ? 1 : 0);
			return false;
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("[TCUpgrade.Harmony] RPC_PickupWallpaperStart: " + ex.Message));
		}
		return true;
	}
}
