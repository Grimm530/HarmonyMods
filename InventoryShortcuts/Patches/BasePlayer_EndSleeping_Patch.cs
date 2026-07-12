using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace InventoryShortcuts.Patches;

/// <summary>Send hotbar UI on spawn/wake (retries until client CUI is ready).</summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.EndSleeping))]
public static class BasePlayer_EndSleeping_Patch
{
    [HarmonyPostfix]
    public static void Postfix(BasePlayer __instance)
    {
        if (__instance?.net?.connection == null) return;
        if (InventoryShortcutsMod.Instance == null) return;
        __instance.StartCoroutine(InventoryShortcutsMod.SendHotbarWithRetry(__instance));
    }
}
