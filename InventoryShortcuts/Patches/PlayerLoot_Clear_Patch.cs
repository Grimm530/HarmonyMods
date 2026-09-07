using HarmonyLib;

namespace InventoryShortcuts.Patches;

/// <summary>
/// When the player closes loot, clear their sent state so the next time they open
/// inventory (Tab or loot) we'll send the buttons again.
/// </summary>
[HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
public static class PlayerLoot_Clear_Patch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerLoot __instance)
    {
        var player = __instance?.GetComponentInParent<BasePlayer>();
        if (player != null)
            InventoryShortcutsMod.ClearPlayerSent(player.userID);
    }
}
