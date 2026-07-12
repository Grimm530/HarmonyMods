using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// When a convoy NPC dies, mark so the next LootableCorpse spawn can be registered as a convoy corpse.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance)
        {
            if (__instance?.net == null) return;
            ulong netId = (ulong)__instance.net.ID.Value;
            if (ConvoyState.GetNpcPresetName(netId) != null)
            {
                ConvoyState.NotifyConvoyNpcDeath(netId);
                ConvoyGrimmNpc.Unregister(netId);
            }
        }
    }
}
