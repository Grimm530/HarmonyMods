using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    /// <summary>Oxide CanBuild — block building in an active DefendableHomes zone.</summary>
    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    public static class Patch_Planner_DoBuild
    {
        [HarmonyPrefix]
        public static bool Prefix(Planner __instance, Construction.Target target, Construction component)
        {
            object result = DHPlugin.Dispatch_CanBuild(__instance, component, target);
            return result == null;
        }
    }
}
