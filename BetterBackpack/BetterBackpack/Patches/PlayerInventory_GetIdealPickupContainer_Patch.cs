using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Existing is enabled and the backpack has a matching stack with room,
/// route loot (e.g. from crates) directly to the backpack instead of main.
/// Fixes: items not stacking to backpack when looting until backpack is opened.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.GetIdealPickupContainer), typeof(Item), typeof(bool))]
internal class PlayerInventory_GetIdealPickupContainer_Patch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, Item item, ref ItemContainer __result)
    {
        if (item == null || item.info.stackable <= 1) return;
        if (!(BetterBackpackConfig.Config?.ExistingEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var player = __instance.GetComponent<BasePlayer>();
        if (player == null || player.IsDead() || player.IsSleeping()) return;

        var backpack = __instance.GetBackpackWithInventory();
        if (backpack?.contents == null) return;

        var existingInBackpack = backpack.contents.FindItemByItemID(item.info.itemid);
        if (existingInBackpack == null) return;
        if (existingInBackpack.amount >= existingInBackpack.MaxStackable()) return;

        // Prefer backpack when we would otherwise put in main (null or main for stacking).
        // Don't override belt - belt has priority for quick-access items.
        if (__result == null || __result == __instance.containerMain)
            __result = backpack.contents;
    }
}
