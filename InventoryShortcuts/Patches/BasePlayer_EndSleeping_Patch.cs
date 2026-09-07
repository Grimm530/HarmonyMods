using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace InventoryShortcuts.Patches;

/// <summary>
/// Send UI on spawn. Parent "Inventory" — game shows/hides it with the inventory panel.
/// AddContainer patch also sends when loot opens (Inventory layer exists). Either way, UI persists.
/// </summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
public static class BasePlayer_EndSleeping_Patch
{
    [HarmonyPostfix]
    public static void Postfix(BasePlayer __instance)
    {
        if (__instance?.net?.connection == null) return;
        var mod = InventoryShortcutsMod.Instance;
        if (mod == null) return;

        // Delay so client CUI is ready (avoids NullRef if Inventory parent not yet initialized)
        __instance.StartCoroutine(SendDelayed(__instance, mod));
    }

    private static IEnumerator SendDelayed(BasePlayer player, InventoryShortcutsMod mod)
    {
        float[] retryDelays = { 0.5f, 2.0f, 5.0f };
        foreach (var delay in retryDelays)
        {
            yield return new WaitForSeconds(delay);
            if (player == null || !player.IsConnected || mod == null) yield break;
            mod.ShowButtons(player);
        }
    }
}
