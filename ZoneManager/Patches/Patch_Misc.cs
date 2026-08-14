using HarmonyLib;
using Network;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
{
    [HarmonyPatch(typeof(Signage), nameof(Signage.CanUpdateSign))]
    public static class Patch_CanUpdateSign
    {
        [HarmonyPrefix]
        public static bool Prefix(Signage __instance, BasePlayer player, ref bool __result)
        {
            object result = ZM.Dispatch_CanUpdateSign(player, __instance);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseOven), "SVSwitch")]
    public static class Patch_OnOvenToggle
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseOven __instance, BaseEntity.RPCMessage msg)
        {
            return ZM.Dispatch_OnOvenToggle(__instance, msg.player) == null;
        }
    }

    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.CanOpenLootPanel))]
    public static class Patch_CanUseVending
    {
        [HarmonyPrefix]
        public static bool Prefix(VendingMachine __instance, BasePlayer player, ref bool __result)
        {
            object result = ZM.Dispatch_CanUseVending(player, __instance);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(StashContainer), nameof(StashContainer.RPC_HideStash))]
    public static class Patch_CanHideStash
    {
        [HarmonyPrefix]
        public static bool Prefix(StashContainer __instance, BaseEntity.RPCMessage rpc)
        {
            return ZM.Dispatch_CanHideStash(rpc.player, __instance) == null;
        }
    }

    [HarmonyPatch(typeof(ServerMgr), "OnPlayerVoice")]
    public static class Patch_OnPlayerVoice
    {
        [HarmonyPrefix]
        public static bool Prefix(Message packet)
        {
            BasePlayer player = packet == null ? null : NetworkPacketEx.Player(packet);
            if (player == null) return true;
            return ZM.Dispatch_OnPlayerVoice(player) == null;
        }
    }
}
