using HarmonyLib;

namespace RecyclerSpeed;

/// <summary>
/// Postfix reduces the recycle think interval by RecyclerSpeedMultiplier.
/// Vanilla: 5s (radtown) or 8s (safezone). Default 2x speed: 2.5s or 4s.
/// Affects all recycler types. No permissions—default group has access.
/// </summary>
[HarmonyPatch(typeof(Recycler), nameof(Recycler.GetRecycleThinkDuration))]
internal class Recycler_GetRecycleThinkDuration_Patch
{
	[HarmonyPostfix]
	private static void Postfix(ref float __result)
	{
		HarmonyConfig.LoadConfig();
		if (HarmonyConfig.Config != null && HarmonyConfig.Config.RecyclerSpeedMultiplier > 0f)
		{
			__result /= HarmonyConfig.Config.RecyclerSpeedMultiplier;
		}
	}
}
