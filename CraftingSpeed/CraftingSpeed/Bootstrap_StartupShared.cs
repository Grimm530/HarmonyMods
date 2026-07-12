using HarmonyLib;
using UnityEngine;

namespace CraftingSpeed;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
internal class Bootstrap_StartupShared
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		Debug.Log((object)"[Harmony] Loaded: CraftingSpeed by Farkas.");
		return true;
	}
}
