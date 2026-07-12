using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, include backpack when finding items by ID (e.g. ammo for reload).
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.FindItemsByItemID), typeof(List<Item>), typeof(int))]
internal class PlayerInventory_FindItemsByItemID_Patch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, List<Item> list, int id)
    {
        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var player = __instance.GetComponent<BasePlayer>();
        if (player == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        backpack.contents.FindItemsByItemID(list, id);
    }
}
