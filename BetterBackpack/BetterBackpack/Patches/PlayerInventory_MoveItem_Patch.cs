using HarmonyLib;

namespace BetterBackpack;

/// <summary>
/// Right-click / drag loot. Logs the RPC and where the item ended up for the debug Steam ID.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.MoveItem))]
internal class PlayerInventory_MoveItem_Patch
{
    internal struct Trace
    {
        public bool Track;
        public ulong Uid;
        public string Item;
        public int AmountBefore;
        public string From;
        public ulong DestContainer;
        public int Slot;
        public uint Amount;
        public int Modifiers;
        public bool ItemMissing;
    }

    [HarmonyPrefix]
    private static void Prefix(PlayerInventory __instance, BaseEntity.RPCMessage msg, ref Trace __state)
    {
        __state = default;
        if (!LootDebug.IsActive) return;
        var player = __instance.baseEntity;
        if (!LootDebug.ShouldLog(player)) return;

        var read = msg.read;
        if (read == null) return;
        long pos = read.Position;
        try
        {
            ItemId id = read.ItemID();
            ItemContainerId destId = read.ItemContainerID();
            int slot = read.Int8();
            uint amount = read.UInt32();
            ItemMoveModifier modifiers = (ItemMoveModifier)read.Int32();

            var item = __instance.FindItemByUID(id);
            __state.Track = true;
            __state.Uid = id.Value;
            __state.DestContainer = destId.Value;
            __state.Slot = slot;
            __state.Amount = amount;
            __state.Modifiers = (int)modifiers;
            if (item == null)
            {
                __state.ItemMissing = true;
                __state.Item = "NOT_FOUND";
                __state.From = "unknown";
            }
            else
            {
                __state.Item = item.info != null ? item.info.shortname : "?";
                __state.AmountBefore = item.amount;
                __state.From = LootDebug.ContainerDesc(item.parent, player);
            }
        }
        finally
        {
            try { read.Position = pos; } catch { }
        }
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerInventory __instance, ref Trace __state)
    {
        if (!__state.Track) return;
        var player = __instance.baseEntity;
        if (player == null) return;

        if (__state.ItemMissing)
        {
            LootDebug.Log(player, $"MoveItem MISSING uid={__state.Uid} dest={__state.DestContainer} slot={__state.Slot} amt={__state.Amount} mods={__state.Modifiers} | {LootDebug.InvSnap(player)}");
            return;
        }

        var item = __instance.FindItemByUID(new ItemId(__state.Uid));
        string after;
        if (item == null || !item.IsValid())
            after = $"{LootDebug.StackedAway} (uid merged; not in inventory/loot)";
        else
            after = $"{LootDebug.ItemDesc(item)} parent={LootDebug.ContainerDesc(item.parent, player)} pos={item.position}";

        var destLabel = __state.DestContainer == 0 ? "quick/GiveItem" : __state.DestContainer.ToString();
        LootDebug.Log(player, $"MoveItem {__state.Item} x{__state.AmountBefore} uid={__state.Uid} from={__state.From} dest={destLabel} slot={__state.Slot} amt={__state.Amount} mods={__state.Modifiers} | after {after} | {LootDebug.InvSnap(player)}");
    }
}
