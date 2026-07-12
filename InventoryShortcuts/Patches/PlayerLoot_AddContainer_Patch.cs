using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace InventoryShortcuts.Patches;

/// <summary>
/// Send UI when loot opens. Parent "Inventory" — game shows/hides with inventory panel.
/// 0.15s delay so client has the loot panel ready before our UI arrives.
/// </summary>
[HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.AddContainer))]
public static class PlayerLoot_AddContainer_Patch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerLoot __instance, ItemContainer container)
    {
        if (container == null) return;
        var mod = InventoryShortcutsMod.Instance;
        if (mod == null) return;

        var player = GetPlayer(__instance);
        if (player == null || player.IsDestroyed) return;

        player.StartCoroutine(SendDelayed(player, mod));
    }

    private static IEnumerator SendDelayed(BasePlayer player, InventoryShortcutsMod mod)
    {
        yield return new WaitForSeconds(0.15f);
        if (player != null && !player.IsDestroyed && player.IsConnected && mod != null)
            mod.ShowButtons(player, includeInventoryPanel: true);
    }

    private static BasePlayer GetPlayer(PlayerLoot loot)
    {
        if (loot == null) return null;
        return loot.GetComponentInParent<BasePlayer>();
    }
}
