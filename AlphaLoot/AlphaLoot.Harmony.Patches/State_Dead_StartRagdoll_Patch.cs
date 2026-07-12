using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch]
public static class State_Dead_StartRagdoll_Patch
{
	[ThreadStatic]
	private static BaseCorpse _lastGen2Corpse;

	private static MethodBase TargetMethod()
	{
		return AccessTools.FirstMethod(AccessTools.TypeByName("Rust.Ai.Gen2.State_Dead"), (MethodInfo m) => m.Name == "StartRagdoll" && m.GetParameters().Length == 0);
	}

	internal static void CaptureCorpse(BaseCorpse corpse)
	{
		_lastGen2Corpse = corpse;
	}

	[HarmonyPostfix]
	private static void Postfix(object __instance)
	{
		BaseCorpse lastGen2Corpse = _lastGen2Corpse;
		_lastGen2Corpse = null;
		if ((Object)(object)lastGen2Corpse == (Object)null)
		{
			return;
		}
		AlphaLootMod instance = AlphaLootMod.Instance;
		if (instance == null || !(lastGen2Corpse is LootableCorpse { containers: not null } lootableCorpse) || lootableCorpse.containers.Length == 0)
		{
			return;
		}
		BaseEntity baseEntity = AccessTools.Field(__instance.GetType(), "Owner")?.GetValue(__instance) as BaseEntity;
		if ((Object)(object)baseEntity == (Object)null)
		{
			return;
		}
		string shortPrefabName = baseEntity.ShortPrefabName;
		if (instance.TryGetNPCProfile(shortPrefabName, out var profile) && profile.Enabled)
		{
			AlphaLootTools.ClearItemContainer(lootableCorpse.containers[0]);
			profile.PopulateLoot(lootableCorpse.containers[0], "");
			AlphaLootConfig config = instance.Config;
			if (config != null && config.DebugLootTable)
			{
				AlphaLootTools.LogLootIfDebug(lootableCorpse.containers[0], "corpse gen2 npc=" + shortPrefabName, shortPrefabName, config.GlobalMultiplier, profile.LootMultiplier);
			}
		}
	}
}
