using System;
using Facepunch;
using HarmonyLib;

namespace IndustrialTransferSpeed.Patches
{
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    public static class Cui_Endtest_PlanterProduction_Patch
    {
        private static string[] ToStringArray(StringView[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] result = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                result[i] = args[i].ToString();
            }
            return result;
        }

        [HarmonyPrefix]
        public static bool Prefix(ConsoleSystem.Arg args)
        {
            StringView[] rawArgs = args?.Args;
            if (rawArgs == null || rawArgs.Length == 0)
            {
                return true;
            }

            string first = rawArgs[0].ToString();
            if (string.IsNullOrEmpty(first) || !first.StartsWith("ITS_PLANTER_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IndustrialTransferSpeedMod.Instance == null)
            {
                return true;
            }

            // HandlePlanterCuiCommand still reads ConsoleSystem.Arg; normalize Args via helper inside mod.
            IndustrialTransferSpeedMod.Instance.HandlePlanterCuiCommand(args, ToStringArray(rawArgs));
            return false;
        }
    }
}
