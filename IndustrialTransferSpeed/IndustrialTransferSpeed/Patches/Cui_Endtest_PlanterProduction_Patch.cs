using System;
using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_PlanterProduction_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            string[] commandArgs = args?.Args;
            if (commandArgs == null || commandArgs.Length == 0)
            {
                return true;
            }

            string first = commandArgs[0];
            if (string.IsNullOrEmpty(first) || !first.StartsWith("ITS_PLANTER_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IndustrialTransferSpeedMod.Instance == null)
            {
                return true;
            }

            IndustrialTransferSpeedMod.Instance.HandlePlanterCuiCommand(args);
            return false;
        }
    }
}
