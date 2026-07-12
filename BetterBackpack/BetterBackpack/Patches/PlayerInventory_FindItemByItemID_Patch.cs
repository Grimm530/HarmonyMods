using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, include backpack when finding a single item by ID (e.g. ammo type switch).
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.FindItemByItemID), typeof(int))]
internal class PlayerInventory_FindItemByItemID_Patch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, int id, ref Item __result)
    {
        if (__result != null) return;
        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var player = __instance.GetComponent<BasePlayer>();
        if (player == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        __result = backpack.contents.FindItemByItemID(id);
    }
}
