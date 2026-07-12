using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>Oxide CanTrainCarCouple — gate front/back coupling while the event train is moving.</summary>
    [HarmonyPatch(typeof(TrainCoupling), nameof(TrainCoupling.TryCouple))]
    public static class Patch_TrainCoupling_TryCouple
    {
        [HarmonyPrefix]
        public static bool Prefix(TrainCoupling __instance, TrainCoupling theirCoupling, ref bool __result)
        {
            if (__instance?.owner == null || theirCoupling?.owner == null)
                return true;

            object result = ATPlugin.Dispatch_CanTrainCarCouple(__instance.owner, theirCoupling.owner);
            if (result == null)
                return true;

            // Oxide false / non-null block => refuse couple
            __result = false;
            return false;
        }
    }
}
