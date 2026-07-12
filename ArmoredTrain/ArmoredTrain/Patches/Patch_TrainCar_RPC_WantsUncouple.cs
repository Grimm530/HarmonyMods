using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>Oxide OnTrainCarUncouple — block uncoupling event wagons.</summary>
    [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.RPC_WantsUncouple), new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_TrainCar_RPC_WantsUncouple
    {
        [HarmonyPrefix]
        public static bool Prefix(TrainCar __instance, BaseEntity.RPCMessage msg)
        {
            object result = ATPlugin.Dispatch_OnTrainCarUncouple(__instance, msg.player);
            return result == null;
        }
    }
}
