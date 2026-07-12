using HarmonyLib;
using HarmonyTests.Lib;
using UnityEngine;

namespace FullRangeAutoturrets.HarmonyPatches;

[HarmonyPatch(typeof(FlameTurret), "MovementUpdate")]
public class FlameTurret_MovementUpdate
{
	public static bool Prepare()
	{
		Main.CheckBootAndInit();
		return (bool)Main.instance.Config.Get("Enabled") && (bool)Main.instance.Config.Get("FlameTurrets.Enabled");
	}

	private static bool Prefix(FlameTurret __instance)
	{
		// MovementUpdate() has no parameters; compute delta from lastMovementUpdate (same as vanilla)
		float lastMovementUpdate = Helpers.GetFieldValue<float>(__instance, "lastMovementUpdate");
		float delta = Time.realtimeSinceStartup - lastMovementUpdate;
		Helpers.SetFieldValue(__instance, "lastMovementUpdate", Time.realtimeSinceStartup);

		// aimDir is private on FlameTurret; use reflection
		Vector3 aimDir = Helpers.GetFieldValue<Vector3>(__instance, "aimDir");
		float num = Mathf.Clamp((float)Main.instance.Config.Get("FlameTurrets.RotationRange"), 0f, 360f);
		if (num == 0f)
		{
			aimDir.y = 0f;
			Helpers.SetFieldValue(__instance, "aimDir", aimDir);
		}
		else
		{
			int fieldValue = Helpers.GetFieldValue<int>(__instance, "turnDir");
			float num2 = num / 2f;
			aimDir += new Vector3(0f, delta * __instance.GetSpinSpeed(), 0f) * (float)fieldValue;
			if (num < 360f)
			{
				if ((double)aimDir.y < (double)num2 && (double)aimDir.y > 0.0 - (double)num2)
				{
					Helpers.SetFieldValue(__instance, "aimDir", aimDir);
					return false;
				}
				fieldValue *= -1;
				Helpers.SetFieldValue(__instance, "turnDir", fieldValue);
				aimDir.y = Mathf.Clamp(aimDir.y, 0f - num2, num2);
				Helpers.SetFieldValue(__instance, "aimDir", aimDir);
				return false;
			}
			Helpers.SetFieldValue(__instance, "turnDir", 1);
			float num3 = ((aimDir.y >= num2) ? (0f - num2) : aimDir.y);
			aimDir.y = Mathf.Clamp(num3, 0f - num2, num2);
			Helpers.SetFieldValue(__instance, "aimDir", aimDir);
		}
		return false;
	}
}
