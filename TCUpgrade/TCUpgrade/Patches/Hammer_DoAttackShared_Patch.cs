using System;
using HarmonyLib;
using UnityEngine;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(Hammer), "DoAttackShared")]
public static class Hammer_DoAttackShared_Patch
{
	private const ulong HammerWallpaperSkin = 3494416562uL;

	private const string LogPrefix = "[TCUpgrade.Patch] ";

	[HarmonyPostfix]
	public static void Postfix(Hammer __instance, HitInfo info)
	{
		try
		{
			if ((Object)(object)info?.HitEntity == (Object)null || TCUpgradeMod.Instance == null || (Object)(object)__instance == (Object)null)
			{
				return;
			}
			BuildingBlock buildingBlock = info.HitEntity as BuildingBlock;
			if ((Object)(object)buildingBlock == (Object)null)
			{
				return;
			}
			BasePlayer ownerPlayer = __instance.GetOwnerPlayer();
			if (!((Object)(object)ownerPlayer == (Object)null))
			{
				Item activeItem = ownerPlayer.GetActiveItem();
				if (!((Object)(object)activeItem?.info == (Object)null) && !(activeItem.info.shortname != "hammer") && activeItem.skin == 3494416562u && IsFloorOrFoundation(buildingBlock))
				{
					RotateWallpaper(buildingBlock);
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[TCUpgrade.Patch] Hammer_DoAttackShared_Patch.Postfix failed: " + ex.Message + "\n" + ex.StackTrace));
		}
	}

	private static bool IsFloorOrFoundation(BuildingBlock block)
	{
		string text = block?.ShortPrefabName ?? "";
		if (!text.Contains("floor"))
		{
			return text.Contains("foundation");
		}
		return true;
	}

	private static bool IsTriangle(BuildingBlock block)
	{
		return (block?.ShortPrefabName ?? "").Contains("triangle");
	}

	private static void RotateWallpaper(BuildingBlock block)
	{
		ulong num = (block.HasWallpaper(0) ? block.wallpaperID : block.wallpaperID2);
		if (num != 0L)
		{
			int num2 = ((!block.HasWallpaper(0)) ? 1 : 0);
			float num3 = ((num2 == 0) ? block.wallpaperRotation : block.wallpaperRotation2);
			float num4 = (IsTriangle(block) ? 120f : 90f);
			float rotation = (num3 + num4) % 360f;
			block.SetWallpaper(num, num2, rotation);
		}
	}
}
