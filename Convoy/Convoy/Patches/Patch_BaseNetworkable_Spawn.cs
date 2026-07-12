using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// When a LootableCorpse spawns and we have a pending convoy NPC death, register the corpse as a convoy corpse.
    /// </summary>
    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class Patch_BaseNetworkable_Spawn
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            if (__instance?.net == null) return;
            if (!(__instance is LootableCorpse)) return;
            ConvoyState.TryRegisterConvoyCorpse((ulong)__instance.net.ID.Value);
        }
    }
}
