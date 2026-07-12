/*
 * IndustrialTransferSpeed - Patches IndustrialConveyor.PostServerLoad
 * Ensures MaxStackSizePerMove is applied when conveyors load from save.
 */

using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(IndustrialConveyor), nameof(IndustrialConveyor.PostServerLoad))]
    public static class IndustrialConveyor_PostServerLoad_Patch
    {
        static void Postfix(IndustrialConveyor __instance)
        {
            if (__instance != null && __instance.IsValid())
            {
                __instance.MaxStackSizePerMove = IndustrialTransferSpeedConfig.Config.MaxStackSizePerMove;
            }
        }
    }
}
