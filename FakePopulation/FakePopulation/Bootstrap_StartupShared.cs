using HarmonyLib;
using UnityEngine;

namespace FakePopulation;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
internal class Bootstrap_StartupShared
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		Debug.Log("[Harmony] Loaded: FakePopulation - shows inflated player count in Steam server browser.");
		return true;
	}
}
