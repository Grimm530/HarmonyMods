using System;
using HarmonyLib;
using UnityEngine;

namespace HarmonyMods.RustGame.Nivex.NoGibs;

public class Manager : IHarmonyModHooks
{
	[HarmonyPatch(typeof(ServerMgr), "OpenConnection")]
	internal class ServerMgr_OpenConnection
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			ResetDebrisGuid = true;
			GameManager.server.FindPrefab("assets/prefabs/misc/xmas/icewalls/wall.external.high.ice.prefab").GetComponent<SimpleBuildingBlock>().debrisPrefab.guid = null;
			GameManager.server.FindPrefab("assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab").GetComponent<SimpleBuildingBlock>().debrisPrefab.guid = null;
			GameManager.server.FindPrefab("assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab").GetComponent<SimpleBuildingBlock>().debrisPrefab.guid = null;
		}
	}

	// Patch TerminateOnClient instead of Kill - avoids "Undefined target method" with optional params.
	// TerminateOnClient(mode) is called from Kill() and controls what destroy mode is sent to clients.
	[HarmonyPatch(typeof(BaseNetworkable), "TerminateOnClient", new Type[] { typeof(BaseNetworkable.DestroyMode) })]
	internal class BaseNetworkable_TerminateOnClient
	{
		[HarmonyPrefix]
		private static void Prefix(ref BaseNetworkable.DestroyMode mode)
		{
			mode = BaseNetworkable.DestroyMode.None;
		}
	}

	private static bool? ResetDebrisGuid;

	void IHarmonyModHooks.OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Debug.LogWarning((object)"[Harmony] Loaded: NoGibs v1.0.0 by nivex");
	}

	void IHarmonyModHooks.OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		Debug.LogWarning((object)"[Harmony] Unloaded: NoGibs v1.0.0 by nivex");
		if (ResetDebrisGuid.HasValue)
		{
			ResetDebrisGuid = null;
			GameManager.server.FindPrefab("assets/prefabs/misc/xmas/icewalls/wall.external.high.ice.prefab").GetComponent<SimpleBuildingBlock>().debrisPrefab.guid = "89828b4bf3e908b4090b31292b2d30d0";
			GameManager.server.FindPrefab("assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab").GetComponent<SimpleBuildingBlock>().debrisPrefab.guid = "708b866f5e465094d9077f5065c4e14d";
			GameManager.server.FindPrefab("assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab").GetComponent<SimpleBuildingBlock>().debrisPrefab.guid = "89828b4bf3e908b4090b31292b2d30d0";
		}
	}
}
