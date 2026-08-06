using HarmonyLib;

namespace RecyclerSpeed;

/// <summary>
/// Postfix reduces recycle duration from GetRecyclerStats by RecyclerSpeedMultiplier.
/// Game update removed GetRecycleThinkDuration; StartRecycling now uses GetRecyclerStats(out efficiency, out duration).
/// </summary>
[HarmonyPatch(typeof(Recycler), nameof(Recycler.GetRecyclerStats))]
internal class Recycler_GetRecyclerStats_Patch
{
	[HarmonyPostfix]
	private static void Postfix(ref float efficiency, ref float duration)
	{
		HarmonyConfig.LoadConfig();
		if (HarmonyConfig.Config != null && HarmonyConfig.Config.RecyclerSpeedMultiplier > 0f)
		{
			duration /= HarmonyConfig.Config.RecyclerSpeedMultiplier;
		}
	}
}
