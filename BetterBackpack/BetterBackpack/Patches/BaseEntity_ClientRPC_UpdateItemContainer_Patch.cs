using System.Collections.Generic;
using HarmonyLib;
using Network;
using ProtoBuf;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When Retrieval is enabled, inject backpack contents into the main inventory data
/// sent to the client. The client receives backpack items as extra slots in main,
/// so vanilla crafting UI, reload checks, etc. see them without needing client-side patches.
/// </summary>
[HarmonyPatch(typeof(BaseEntity), nameof(BaseEntity.ClientRPC), typeof(RpcTarget), typeof(UpdateItemContainer))]
internal static class BaseEntity_ClientRPC_UpdateItemContainer_Patch
{
    private const int MainInventorySlots = 24;

    [HarmonyPrefix]
    private static void Prefix(BaseEntity __instance, UpdateItemContainer arg1)
    {
        if (arg1 == null || arg1.container == null || arg1.container.Count == 0) return;
        if (arg1.type != (int)PlayerInventory.Type.Main) return;

        var player = __instance as BasePlayer;
        if (player?.inventory == null) return;

        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        var backpack = player.inventory.GetBackpackWithInventory();
        if (backpack?.contents?.itemList == null || backpack.contents.itemList.Count == 0) return;

        var containerData = arg1.container[0];
        if (containerData == null) return;
        // ProtoBuf may leave contents null when empty; only inject when we can add to the list
        if (containerData.contents == null) return;

        var nextSlot = GetNextInvisibleSlot(containerData);
        var itemsAdded = 0;

        foreach (var item in backpack.contents.itemList)
        {
            if (item == null || !item.IsValid()) continue;

            var itemData = item.Save(bIncludeContainer: false, bIncludeOwners: true);
            if (itemData == null) continue;

            itemData.slot = nextSlot++;
            if (itemData.UID.Value == 0)
                itemData.UID = new ItemId(ulong.MaxValue - (ulong)nextSlot);

            containerData.contents.Add(itemData);
            itemsAdded++;
        }

        if (itemsAdded > 0 && containerData.slots >= MainInventorySlots)
            containerData.slots = nextSlot;
    }

    private static int GetNextInvisibleSlot(ProtoBuf.ItemContainer containerData)
    {
        var highest = MainInventorySlots - 1;
        if (containerData.contents != null)
        {
            foreach (var item in containerData.contents)
            {
                if (item != null && item.slot > highest)
                    highest = item.slot;
            }
        }
        return highest + 1;
    }
}
