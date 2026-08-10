using HarmonyLib;

namespace CraftingSpeed;

[HarmonyPatch(typeof(ItemCrafter), "GetScaledDuration")]
internal class ItemCrafter_GetScaledDuration
{
	/// <summary>
	/// Postfix divides the crafting duration by CraftingSpeedMultiplier.
	/// </summary>
	[HarmonyPostfix]
	private static void Postfix(ref float __result)
	{
		if (__result <= 0f) return;
		HarmonyConfig.LoadConfig();
		float mult = HarmonyConfig.Config?.CraftingSpeedMultiplier ?? 0f;
		if (mult > 0f)
			__result /= mult;
	}
}
