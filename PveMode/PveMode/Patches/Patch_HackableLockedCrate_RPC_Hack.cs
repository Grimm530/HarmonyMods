using HarmonyLib;

namespace PveModeHarmony.Patches
{
    /// <summary>
    /// Blocks starting a hack on an event-owned locked crate for non-owner, non-team players.
    /// Mirrors Oxide PveMode's CanHackCrate hook.
    /// </summary>
    [HarmonyPatch(typeof(HackableLockedCrate), nameof(HackableLockedCrate.RPC_Hack), new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_HackableLockedCrate_RPC_Hack
    {
        [HarmonyPrefix]
        public static bool Prefix(HackableLockedCrate __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return true;
            object result = PveModeManager.CanHackCrate(msg.player, __instance);
            return !(result is bool blocked && blocked);
        }
    }
}
