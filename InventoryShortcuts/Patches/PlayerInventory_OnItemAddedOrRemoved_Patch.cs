using HarmonyLib;

namespace InventoryShortcuts.Patches;

/// <summary>
/// Shows hotbar shortcut buttons when the player moves or adds items in their inventory.
/// This fires when Tab is open and they drag an item, or when looting adds items.
/// Together with AddContainer, this detects inventory/menu being used.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.OnItemAddedOrRemoved))]
public static class PlayerInventory_OnItemAddedOrRemoved_Patch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerInventory __instance)
    {
        var mod = InventoryShortcutsMod.Instance;
        if (mod == null) return;

        var player = __instance.GetComponent<BasePlayer>();
        if (player?.net?.connection == null) return;
        if (player.IsDead() || player.IsSleeping()) return;

        mod.ShowButtonsIfNeeded(player);
    }
}
