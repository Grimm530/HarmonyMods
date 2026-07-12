using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of ArmoredTrain CanHackCrate(BasePlayer, HackableLockedCrate): loot-lock / aggression
    /// enforcement on event hackable crates. Non-null result -> block the hack RPC.
    /// </summary>
    [HarmonyPatch(typeof(HackableLockedCrate), nameof(HackableLockedCrate.RPC_Hack), new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_HackableLockedCrate_RPC_Hack
    {
        [HarmonyPrefix]
        public static bool Prefix(HackableLockedCrate __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return true;
            object result = ATPlugin.Dispatch_CanHack(msg.player, __instance);
            return result == null;
        }
    }
}
