using HarmonyLib;

namespace CraftingSpeed;

[HarmonyPatch(typeof(ItemCrafter), "GetScaledDuration")]
internal class ItemCrafter_GetScaledDuration
{
	/// <summary>
	/// Postfix divides the crafting duration by CraftingSpeedMultiplier.
	/// The original transpiler targeted Ldfld "time" which only exists inside ItemBlueprint.GetCraftTime(),
	/// not in GetScaledDuration, so it never matched. Using a Postfix on the return value works correctly.
	/// </summary>
	[HarmonyPostfix]
	private static void Postfix(ref float __result)
	{
		HarmonyConfig.LoadConfig();
		if (HarmonyConfig.Config != null && HarmonyConfig.Config.CraftingSpeedMultiplier > 0f)
		{
			__result /= HarmonyConfig.Config.CraftingSpeedMultiplier;
		}
	}
}
