using HarmonyLib;
using BNPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc.Patches
{
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class Patch_BaseNetworkable_Kill
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            BNPlugin.Dispatch_OnEntityKill(__instance);
        }
    }
}
