using HarmonyLib;
using DHPlugin = Harmony.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    /// <summary>Oxide OnEntityKill(BuildingBlock) — foundation destroyed ends the event when none remain.</summary>
    public static class Patch_BaseNetworkable_Kill
    {
        public static void Prefix(BaseNetworkable __instance)
        {
            DHPlugin.Dispatch_OnEntityKill(__instance);
        }
    }
}
