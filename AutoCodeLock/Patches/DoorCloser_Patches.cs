using HarmonyLib;
using UnityEngine;

namespace AutoCodeLockHarmony.Patches
{
    [HarmonyPatch(typeof(DoorCloser), nameof(DoorCloser.SendClose))]
    internal static class DoorCloser_SendClose_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(DoorCloser __instance)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return true;

            try
            {
                if (plugin.ShouldDisableDoorCloser(__instance))
                    return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] DoorCloser.SendClose: " + ex.Message);
            }
            return true;
        }
    }

    /// <summary>Oxide CanPickupEntity for door closers — DoorCloser.RPC_Take.</summary>
    [HarmonyPatch(typeof(DoorCloser), "RPC_Take")]
    internal static class DoorCloser_RPC_Take_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(DoorCloser __instance, BaseEntity.RPCMessage rpc)
        {
            var plugin = AutoCodeLockMod.Instance?.Plugin;
            if (plugin == null || __instance == null) return true;

            try
            {
                BasePlayer player = rpc.player;
                object blocked = plugin.CanPickupEntity(player, __instance);
                if (blocked is bool b && !b)
                    return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AutoCodeLock] CanPickupEntity: " + ex.Message);
            }
            return true;
        }
    }
}
