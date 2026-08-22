using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    /// <summary>Oxide OnEntityKill(BuildingBlock) — foundation destroyed ends the event when none remain.</summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class Patch_BaseNetworkable_Kill
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            DHPlugin.Dispatch_OnEntityKill(__instance);
        }
    }
}
