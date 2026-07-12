using HarmonyLib;

namespace FullRangeAutoturrets.HarmonyPatches;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
internal class Bootstrap_StartupShared
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		Main.CheckBootAndInit();
		return true;
	}
}
