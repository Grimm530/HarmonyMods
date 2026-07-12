using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, include backpack in GetAmount so crafting UI and ingredient checks see backpack items.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.GetAmount), typeof(int), typeof(bool))]
internal class PlayerInventory_GetAmount_Patch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, int itemid, bool includeBackpack, ref int __result)
    {
        if (includeBackpack) return; // Caller already asked for backpack
        if (itemid == 0) return;
        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var player = __instance.GetComponent<BasePlayer>();
        if (player == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        __result += backpack.contents.GetAmount(itemid, onlyUsableAmounts: true);
    }
}
