using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, include backpack contents when checking if player has enough for crafting.
/// </summary>
[HarmonyPatch(typeof(ItemCrafter), "DoesHaveUsableItem")]
internal class ItemCrafter_DoesHaveUsableItem_Patch
{
    [HarmonyPostfix]
    private static void Postfix(ItemCrafter __instance, int item, int iAmount, ref bool __result)
    {
        if (__result) return;
        var owner = __instance.owner;
        if (owner == null) return;
        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;

        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var prefs = mod.GetOrCreatePrefs(owner);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        var backpack = owner.inventory.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        var have = 0;
        var list = __instance.containers;
        if (list != null)
        {
            for (var i = 0; i < list.Count; i++)
                have += list[i].GetAmount(item, onlyUsableAmounts: true);
        }
        have += backpack.contents.GetAmount(item, onlyUsableAmounts: true);
        __result = have >= iAmount;
    }
}
