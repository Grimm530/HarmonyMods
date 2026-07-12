using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BetterAirDrop;

[HarmonyPatch(typeof(SupplySignal), "Explode")]
internal class SupplySignal_Explode
{
	private const string SupplyDropPrefab = "assets/prefabs/misc/supply drop/supply_drop.prefab";

	[HarmonyPrefix]
	private static bool Prefix(SupplySignal __instance)
	{
		HarmonyConfig.LoadConfig();
		if (!HarmonyConfig.Config.InstantSupplyDrops)
		{
			return true;
		}
		BasePlayer player = __instance.creatorEntity as BasePlayer;
		Vector3 pos;
		if (player != null && !player.IsDestroyed)
		{
			Vector3 origin = player.eyes != null ? player.eyes.position : player.transform.position + Vector3.up * 1.5f;
			Vector3 forward = player.eyes != null ? player.eyes.BodyForward() : player.transform.forward;
			pos = origin + forward.normalized * 3f;
		}
		else
		{
			pos = __instance.transform.position + Vector3.up * 2f;
		}
		float terrain = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(pos) : pos.y;
		if (HarmonyConfig.Config.InstantSupplyDropFallsFromSky)
		{
			pos.y = terrain + HarmonyConfig.Config.PlaneAdditionalHeight;
		}
		else
		{
			pos.y = Mathf.Max(pos.y, terrain + 1.5f);
		}
		SupplyDrop ent = GameManager.server.CreateEntity(SupplyDropPrefab, pos) as SupplyDrop;
		if (ent != null)
		{
			if (player != null && player.userID != 0UL)
			{
				ent.OwnerID = player.userID;
			}
			ent.Spawn();
		}
		if (__instance != null && !__instance.IsDestroyed)
		{
			if (HarmonyConfig.Config.RemoveSupplySignalSmoke)
			{
				__instance.Kill();
			}
			else
			{
				__instance.SetFlag(BaseEntity.Flags.On, true);
				__instance.SendNetworkUpdateImmediate();
				__instance.Invoke("FinishUp", 210f);
			}
		}
		return false;
	}

	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		HarmonyConfig.LoadConfig();
		List<CodeInstruction> list = new List<CodeInstruction>(instructions);
		if (HarmonyConfig.Config.ExactAirDrop)
		{
			foreach (CodeInstruction item in list)
			{
				if (item.opcode == OpCodes.Ldc_R4 && ((float)item.operand == -20f || (float)item.operand == 20f))
				{
					item.operand = 0f;
				}
			}
		}
		if (HarmonyConfig.Config.RemoveSupplySignalSmoke)
		{
			int num = list.FindIndex((CodeInstruction x) => x.opcode == OpCodes.Call && ((MethodInfo)x.operand).Name == "SetFlag");
			if (num != -1)
			{
				list.RemoveRange(num - 5, 6);
			}
		}
		return list;
	}
}
