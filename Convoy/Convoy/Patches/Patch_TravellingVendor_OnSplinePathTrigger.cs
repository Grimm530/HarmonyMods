using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// Convoy TravellingVendors are kinematic on-rails (DoAI=false). Vanilla road TriggerPath volumes
    /// still call OnSplinePathTrigger → StopSplineMovement, which NREs on null/empty currentPath and
    /// flips isKinematic=false (PhysX illegal collision shapes). Oxide blocked this via
    /// OnEntityEnter(TriggerPath, TravellingVendor) returning true; mirror that here.
    /// </summary>
    [HarmonyPatch(typeof(TravellingVendor), nameof(TravellingVendor.OnSplinePathTrigger))]
    public static class Patch_TravellingVendor_OnSplinePathTrigger
    {
        [HarmonyPrefix]
        public static bool Prefix(TravellingVendor __instance)
        {
            if (__instance == null || __instance.net == null) return true;
            if (ConvoyState.IsConvoyEntity((ulong)__instance.net.ID.Value))
                return false;
            return true;
        }
    }
}
