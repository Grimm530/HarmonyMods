using HarmonyLib;
using UnityEngine;

namespace BetterAirDrop;

[HarmonyPatch(typeof(SupplyDrop), "ServerInit")]
internal class SupplyDrop_ServerInit
{
	[HarmonyPostfix]
	private static void Postfix(ref SupplyDrop __instance)
	{
		HarmonyConfig.LoadConfig();
		((Component)__instance).gameObject.GetComponent<Rigidbody>().drag = HarmonyConfig.Config.CrateAirResistance;
	}
}
