using HarmonyLib;
using UnityEngine;

namespace FullRangeAutoturrets.HarmonyPatches;

[HarmonyPatch(typeof(AutoTurret), "InFiringArc")]
public class AutoTurret_InFiringArc
{
	public static bool Prepare()
	{
		Main.CheckBootAndInit();
		return (bool)Main.instance.Config.Get("Enabled") && (bool)Main.instance.Config.Get("AutoTurrets.Enabled");
	}

	private static bool Prefix(ref bool __result, ref AutoTurret __instance, BaseCombatEntity potentialtarget)
	{
		float num = Mathf.Clamp((float)Main.instance.Config.Get("AutoTurrets.DetectRange"), 0f, 360f);
		if (num == 0f)
		{
			__result = false;
		}
		else if (num == 360f)
		{
			__result = true;
		}
		else
		{
			__result = (double)Mathf.Abs(__instance.AngleToTarget(potentialtarget)) <= (double)num / 2.0;
		}
		return false;
	}
}
