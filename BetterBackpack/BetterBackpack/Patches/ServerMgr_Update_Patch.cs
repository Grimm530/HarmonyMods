using HarmonyLib;

namespace BetterBackpack;

/// <summary>
/// Process deferred backpack moves once per server tick to avoid re-entrancy in OnItemAddedOrRemoved.
/// </summary>
[HarmonyPatch(typeof(ServerMgr), "Update")]
internal class ServerMgr_Update_Patch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        LootDebug.Tick();
        // ProcessDeferredMoves no-ops quickly when no deferred backpack stacking is pending.
        PlayerInventory_OnItemAddedOrRemoved_Patch.ProcessDeferredMoves();
    }
}
