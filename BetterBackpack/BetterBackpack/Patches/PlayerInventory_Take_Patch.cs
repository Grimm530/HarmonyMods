using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, allow Take to also take from backpack (e.g. construction, workbench).
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.Take), typeof(List<Item>), typeof(int), typeof(int))]
internal class PlayerInventory_Take_Patch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, List<Item> collect, int itemid, int amount, ref int __result)
    {
        if (amount <= 0) return;
        var player = __instance.GetComponent<BasePlayer>();
        if (player == null) return;

        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        var need = amount - __result;
        if (need <= 0) return;

        var taken = backpack.contents.Take(collect, itemid, need);
        __result += taken;
    }
}
