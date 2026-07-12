using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// When the convoy event is locked to a team, only that team can start hacking convoy hackable locked crates.
    /// </summary>
    [HarmonyPatch(typeof(HackableLockedCrate), nameof(HackableLockedCrate.RPC_Hack), new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_HackableLockedCrate_RPC_Hack
    {
        [HarmonyPrefix]
        public static bool Prefix(HackableLockedCrate __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance?.net == null || msg.player == null) return true;
            ulong netId = (ulong)__instance.net.ID.Value;
            if (!ConvoyState.IsConvoyEntity(netId)) return true;

            var mod = ConvoyMod.Instance;
            if (mod?.Config?.LootSettings != null)
                ConvoyState.EnsureLockExpiry(mod.Config.LootSettings.EventLockUnlockAfterSeconds);

            if (ConvoyState.LockedTeamId == 0) return true;
            if (ConvoyState.IsLockedToPlayerTeam(msg.player)) return true;

            return false;
        }
    }
}
