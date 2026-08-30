using HarmonyLib;

namespace BetterBackpack;

/// <summary>
/// Quick-loot / pickup path (invalid container id in MoveItem ends here).
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.GiveItem), typeof(Item), typeof(ItemMoveModifier), typeof(ItemContainer), typeof(GiveItemOptions))]
internal class PlayerInventory_GiveItem_Patch
{
    internal struct Trace
    {
        public bool Track;
        public ulong Uid;
        public string Item;
        public int AmountBefore;
        public string From;
        public string HintContainer;
    }

    [HarmonyPrefix]
    private static void Prefix(PlayerInventory __instance, Item item, ItemMoveModifier modifiers, ItemContainer container, GiveItemOptions options, ref Trace __state)
    {
        __state = default;
        if (!LootDebug.IsActive) return;
        var player = __instance.baseEntity;
        if (!LootDebug.ShouldLog(player)) return;
        if (item == null) return;

        __state.Track = true;
        __state.Uid = item.uid.Value;
        __state.Item = item.info != null ? item.info.shortname : "?";
        __state.AmountBefore = item.amount;
        __state.From = LootDebug.ContainerDesc(item.parent, player);
        __state.HintContainer = LootDebug.ContainerDesc(container, player);
        LootDebug.Log(player, $"GiveItem start {__state.Item} x{__state.AmountBefore} uid={__state.Uid} from={__state.From} hint={__state.HintContainer} mods={modifiers} opts={options} | {LootDebug.InvSnap(player)}");
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, Item item, bool __result, ref Trace __state)
    {
        if (!__state.Track) return;
        var player = __instance.baseEntity;
        if (player == null) return;

        string after;
        if (item == null || !item.IsValid())
            after = "GONE";
        else
            after = $"{LootDebug.ItemDesc(item)} parent={LootDebug.ContainerDesc(item.parent, player)} pos={item.position}";

        LootDebug.Log(player, $"GiveItem {(__result ? "OK" : "FAIL")} {__state.Item} x{__state.AmountBefore} uid={__state.Uid} | after {after} | {LootDebug.InvSnap(player)}");
    }
}
