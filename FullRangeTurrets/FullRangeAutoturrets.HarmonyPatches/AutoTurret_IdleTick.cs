using HarmonyLib;
using HarmonyTests.Lib;
using UnityEngine;

namespace FullRangeAutoturrets.HarmonyPatches;

[HarmonyPatch(typeof(AutoTurret), "IdleTick")]
public class AutoTurret_IdleTick
{
	public static bool Prepare()
	{
		Main.CheckBootAndInit();
		return (bool)Main.instance.Config.Get("Enabled") && (bool)Main.instance.Config.Get("AutoTurrets.Enabled");
	}

	private static bool Prefix(AutoTurret __instance)
	{
		// nextIdleAimTime is double in game; use reflection (null-safe via Helpers)
		double nextIdleAimTime = Helpers.GetFieldValue<double>(__instance, "nextIdleAimTime");
		if (nextIdleAimTime < 10.0)
		{
			return true;
		}
		double now = Time.realtimeSinceStartup;
		if (now <= nextIdleAimTime)
		{
			return true;
		}
		float num = Mathf.Clamp((float)Main.instance.Config.Get("AutoTurrets.RotationRange"), 0f, 360f);
		double nextTime = now + (double)Random.Range((num / 2f < 45f) ? 2f : 4f, (num / 2f < 45f) ? 3f : 5f);
		Vector3 val = Quaternion.LookRotation(__instance.transform.forward, Vector3.up) * Quaternion.AngleAxis(0f, Vector3.up) * Vector3.forward;
		if (num != 0f)
		{
			float num2 = Random.Range(0f - num / 2f, 0f - ((num > 90f) ? 20f : 0f));
			float num3 = Random.Range((num > 90f) ? 20f : 0f, num / 2f);
			val = Quaternion.LookRotation(__instance.transform.forward, Vector3.up) * Quaternion.AngleAxis(Random.Range(num2, num3), Vector3.up) * Vector3.forward;
		}
		Helpers.SetFieldValue(__instance, "targetAimDir", val);
		Helpers.SetFieldValue(__instance, "nextIdleAimTime", nextTime);
		return true;
	}
}
