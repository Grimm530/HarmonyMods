using HarmonyLib;
using UnityEngine;

namespace MixingSpeed;

[HarmonyPatch(typeof(Bootstrap), "StartupShared")]
internal class Bootstrap_StartupShared
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		Debug.Log((object)"[Harmony] Loaded: MixingSpeed by Farkas.");
		return true;
	}
}
