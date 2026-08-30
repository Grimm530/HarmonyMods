using HarmonyLib;

namespace BetterBackpack;

/// <summary>
/// Snapshot this player's inventory (including worn backpack contents) at death.
/// Explains "two bags": vanilla corpse + dropped worn backpack.
/// </summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die))]
internal class BasePlayer_Die_Patch
{
    [HarmonyPrefix]
    private static void Prefix(BasePlayer __instance)
    {
        if (!LootDebug.IsActive || __instance == null) return;
        LootDebug.DumpInventory(__instance, "Die");
    }
}
