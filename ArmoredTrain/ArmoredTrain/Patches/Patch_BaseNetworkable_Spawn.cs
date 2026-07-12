using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of ArmoredTrain OnEntitySpawned(HelicopterDebris) / OnEntitySpawned(LootContainer):
    /// clean up bradley gibs inside the event zone and schedule bradley_crate burn.
    /// </summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class Patch_BaseNetworkable_Spawn
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            ATPlugin.Dispatch_Spawned(__instance);
        }
    }
}
