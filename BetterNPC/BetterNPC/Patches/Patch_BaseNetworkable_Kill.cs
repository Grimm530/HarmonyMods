using HarmonyLib;
using BNPlugin = Harmony.Plugins.BetterNpc;

namespace BetterNpc.Patches
{
    public static class Patch_BaseNetworkable_Kill
    {
        public static void Prefix(BaseNetworkable __instance)
        {
            BNPlugin.Dispatch_OnEntityKill(__instance);
        }
    }
}
