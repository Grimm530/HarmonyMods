using HarmonyLib;
using UnityEngine;

namespace BetterAirDrop;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
internal class Bootstrap_StartupShared
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		Debug.Log((object)"[Harmony] Loaded: BetterAirDrop by Farkas.");
		return true;
	}
}
