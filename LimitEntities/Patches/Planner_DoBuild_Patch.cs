using HarmonyLib;

namespace LimitEntities.Patches
{
    [HarmonyPatch(typeof(Planner), nameof(Planner.DoBuild), typeof(Construction.Target), typeof(Construction))]
    internal static class Planner_DoBuild_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Planner __instance, Construction.Target target, Construction component)
        {
            var service = LimitEntitiesMod.Service;
            if (service == null || !service.IsReady) return true;

            BasePlayer player = __instance?.GetOwnerPlayer();
            object result = service.HandleCanBuild(player, component, target);
            // Oxide: non-null (typically false) blocks build
            return result == null;
        }
    }
}
