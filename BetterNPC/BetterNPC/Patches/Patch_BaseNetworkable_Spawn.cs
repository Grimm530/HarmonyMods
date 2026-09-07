using HarmonyLib;
using BNPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class Patch_BaseNetworkable_Spawn
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            BNPlugin.Dispatch_Spawned(__instance);
        }
    }
}
