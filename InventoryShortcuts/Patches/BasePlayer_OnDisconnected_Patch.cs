using HarmonyLib;

namespace InventoryShortcuts.Patches;

/// <summary>
/// On disconnect: destroy our UI and clear sent state so player gets buttons again on reconnect.
/// </summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
public static class BasePlayer_OnDisconnected_Patch
{
    [HarmonyPostfix]
    public static void Postfix(BasePlayer __instance)
    {
        if (__instance == null) return;
        var mod = InventoryShortcutsMod.Instance;
        if (mod != null)
            mod.DestroyUi(__instance);
    }
}
