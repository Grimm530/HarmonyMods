using HarmonyLib;

namespace GrimmCuiHarmony.Patches
{
    [HarmonyPatch(typeof(global::cui), nameof(global::cui.endtest))]
    internal static class Cui_Endtest_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(ConsoleSystem.Arg args)
        {
            if (GrimmCui.TryRouteEndtest(args))
                return false;
            return true;
        }
    }
}
