using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of ArmoredTrain OnSwitchToggle (block) + OnSwitchToggled (post). RPC_Switch is the
    /// player-initiated toggle RPC for the handbrake switch that controls the event train.
    /// </summary>
    [HarmonyPatch(typeof(ElectricSwitch), "RPC_Switch", new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_ElectricSwitch_RPC_Switch
    {
        [HarmonyPrefix]
        public static bool Prefix(ElectricSwitch __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return true;
            object result = ATPlugin.Dispatch_OnSwitchToggle(__instance, msg.player);
            return result == null;
        }

        [HarmonyPostfix]
        public static void Postfix(ElectricSwitch __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return;
            ATPlugin.Dispatch_OnSwitchToggled(__instance, msg.player);
        }
    }
}
