using System.Reflection;
using HarmonyLib;

namespace RecyclerSpeed;

/// <summary>
/// When player closes loot and the source was a Recycler, destroy our CUI overlay.
/// Uses reflection for baseEntity—EntityComponent.baseEntity is protected, inaccessible from external assembly.
/// </summary>
[HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
internal class PlayerLoot_Clear_Patch
{
	private static readonly PropertyInfo BaseEntityProp = typeof(PlayerLoot).BaseType
		?.GetProperty("baseEntity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

	[HarmonyPrefix]
	private static void Prefix(PlayerLoot __instance)
	{
		if (__instance.entitySource is Recycler && BaseEntityProp?.GetValue(__instance) is BasePlayer player)
		{
			RecyclerSpeedMod.Instance?.OnPlayerClosedRecyclerLoot(player);
		}
	}
}
