using HarmonyLib;
using UnityEngine;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Oxide OnEntityEnter(TriggerTrainCollisions, TrainCar) — destroy non-event wagons that hit the train.
    /// Patches TriggerTrainCollisions.OnObjectAdded (server path that registers colliding TrainCars).
    /// </summary>
    [HarmonyPatch(typeof(TriggerTrainCollisions), nameof(TriggerTrainCollisions.OnObjectAdded))]
    public static class Patch_TriggerTrainCollisions_OnObjectAdded
    {
        [HarmonyPostfix]
        public static void Postfix(TriggerTrainCollisions __instance, GameObject obj, Collider col)
        {
            if (__instance == null || obj == null) return;
            TrainCar trainCar = obj.GetComponentInParent<TrainCar>();
            if (trainCar == null) return;
            ATPlugin.Dispatch_OnEntityEnter(__instance, trainCar);
        }
    }
}
