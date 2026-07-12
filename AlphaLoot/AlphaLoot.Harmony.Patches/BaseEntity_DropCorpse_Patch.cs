using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AlphaLoot.Harmony.Patches;

[HarmonyPatch(typeof(BaseEntity))]
public static class BaseEntity_DropCorpse_Patch
{
	private static MethodBase TargetMethod()
	{
		MethodInfo methodInfo = AccessTools.Method(typeof(BaseEntity), "DropCorpse", new Type[3]
		{
			typeof(string),
			typeof(BasePlayer.PlayerFlags),
			typeof(ModelState)
		});
		if (methodInfo != null)
		{
			return methodInfo;
		}
		return AccessTools.Method(typeof(BaseEntity), "DropCorpse", new Type[5]
		{
			typeof(string),
			typeof(Vector3),
			typeof(Quaternion),
			typeof(BasePlayer.PlayerFlags),
			typeof(ModelState)
		});
	}

	[HarmonyPostfix]
	private static void Postfix(BaseCorpse __result)
	{
		if ((Object)(object)__result == (Object)null || AlphaLootMod.Instance == null)
		{
			return;
		}
		StackTrace stackTrace = new StackTrace(1, fNeedFileInfo: false);
		for (int i = 0; i < Mathf.Min(stackTrace.FrameCount, 8); i++)
		{
			MethodBase methodBase = stackTrace.GetFrame(i)?.GetMethod();
			if (methodBase?.DeclaringType?.FullName == "Rust.Ai.Gen2.State_Dead" && methodBase.Name == "StartRagdoll")
			{
				State_Dead_StartRagdoll_Patch.CaptureCorpse(__result);
				break;
			}
		}
	}
}
