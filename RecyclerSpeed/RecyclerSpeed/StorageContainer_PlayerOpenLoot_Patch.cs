using HarmonyLib;

namespace RecyclerSpeed;

/// <summary>
/// When player opens a Recycler, send our CUI overlay to cover the static efficiency text.
/// </summary>
[HarmonyPatch(typeof(StorageContainer), nameof(StorageContainer.PlayerOpenLoot))]
internal class StorageContainer_PlayerOpenLoot_Patch
{
	[HarmonyPostfix]
	private static void Postfix(StorageContainer __instance, BasePlayer player, bool __result)
	{
		if (!__result || __instance is not Recycler recycler)
			return;

		HarmonyConfig.LoadConfig();
		if (HarmonyConfig.Config?.Debug ?? false)
			UnityEngine.Debug.Log("[RecyclerSpeed] Patch fired: Recycler opened by " + (player?.displayName ?? "null"));

		RecyclerSpeedUI.SendOverlay(player, recycler);
	}
}
