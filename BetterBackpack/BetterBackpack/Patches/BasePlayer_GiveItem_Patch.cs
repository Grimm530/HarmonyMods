using HarmonyLib;

namespace BetterBackpack;

/// <summary>
/// World pickups keep a world entity until MoveToContainer, but harvested items are
/// created with no parent. Mark those as loot so Existing still yanks them after they
/// land in main/belt. Splits and swaps never go through this overload.
/// </summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.GiveItem), typeof(Item), typeof(BaseEntity.GiveItemReason), typeof(GiveItemOptions))]
internal class BasePlayer_GiveItem_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Item item, BaseEntity.GiveItemReason reason)
    {
        if (item == null) return;
        if (reason != BaseEntity.GiveItemReason.PickedUp && reason != BaseEntity.GiveItemReason.ResourceHarvested)
            return;
        Item_MoveToContainer_Patch.MarkExternalPickup(item);
    }

    [HarmonyPostfix]
    private static void Postfix(BasePlayer __instance, Item item)
    {
        if (item == null || !item.IsValid()) return;
        var inv = __instance?.inventory;
        var parent = item.parent;
        if (inv != null && (parent == inv.containerMain || parent == inv.containerBelt))
            return;
        Item_MoveToContainer_Patch.ClearExternalPickupMark(item);
    }
}
