using HarmonyLib;
using UnityEngine;

namespace BagCooldowns;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
internal class Bootstrap_StartupShared
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		Debug.Log((object)"[Harmony] Loaded: BagCooldowns by Farkas.");
		return true;
	}
}
