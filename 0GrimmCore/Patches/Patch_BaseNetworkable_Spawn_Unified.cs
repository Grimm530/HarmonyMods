using HarmonyLib;

namespace GrimmCoreHarmony.Patches
{
    /// <summary>
    /// Single owner postfix for BaseNetworkable.Spawn(). All mods register via GrimmCoreSpawnDispatcher.
    /// Matches Oxide.Rust OnEntitySpawned (simple postfix on Spawn, this-pointer only).
    /// </summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    internal static class Patch_BaseNetworkable_Spawn_Unified
    {
        [HarmonyPostfix]
        private static void Postfix(BaseNetworkable __instance)
        {
            GrimmCoreSpawnDispatcher.InvokePostfix(__instance);
        }
    }
}
