using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When backpack contents change and Retrieval is on, mark containerMain dirty
/// so ItemRetriever re-sends Main with supplier items (craft UI / reload).
/// </summary>
[HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.MarkDirty))]
internal class ItemContainer_MarkDirty_Patch
{
    [HarmonyPostfix]
    private static void Postfix(ItemContainer __instance)
    {
        var parent = __instance.parent;
        if (parent == null || !parent.IsBackpack()) return;

        var wearContainer = parent.parent;
        if (wearContainer == null || !wearContainer.HasFlag(ItemContainer.Flag.Clothing)) return;

        var player = wearContainer.playerOwner;
        if (player?.inventory == null) return;

        if (!(BetterBackpackConfig.Config?.RetrievalEnabled ?? true)) return;
        var mod = BetterBackpackMod.Instance;
        if (mod == null) return;
        var prefs = mod.GetOrCreatePrefs(player);
        if (prefs == null || !prefs.RetrievalEnabled) return;

        if (player.inventory.containerMain != null)
            player.inventory.containerMain.dirty = true;
    }
}
