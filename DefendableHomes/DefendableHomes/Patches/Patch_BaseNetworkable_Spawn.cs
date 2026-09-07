using HarmonyLib;
using DHPlugin = Harmony.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class Patch_BaseNetworkable_Spawn
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            DHPlugin.Dispatch_Spawned(__instance);
        }
    }
}
