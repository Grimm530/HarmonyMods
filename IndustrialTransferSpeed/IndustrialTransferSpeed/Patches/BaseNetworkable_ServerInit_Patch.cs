/*
 * IndustrialTransferSpeed - Patches BaseNetworkable.ServerInit
 * When an IndustrialConveyor spawns (new or loaded), set MaxStackSizePerMove from config.
 */

using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.ServerInit))]
    public static class BaseNetworkable_ServerInit_Patch
    {
        static void Postfix(BaseNetworkable __instance)
        {
            if (__instance is IndustrialConveyor conveyor && conveyor.IsValid())
            {
                conveyor.MaxStackSizePerMove = IndustrialTransferSpeedConfig.Config.MaxStackSizePerMove;
            }
        }
    }
}
