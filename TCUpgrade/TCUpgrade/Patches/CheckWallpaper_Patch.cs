using HarmonyLib;

namespace TCUpgrade.Patches;

[HarmonyPatch(typeof(BuildingBlock), "CheckWallpaper")]
public static class CheckWallpaper_Patch
{
	[HarmonyPrefix]
	public static bool Prefix()
	{
		if (TCUpgradeMod.Instance != null)
		{
			return !TCUpgradeMod.Instance.ForceBothSides;
		}
		return true;
	}
}
