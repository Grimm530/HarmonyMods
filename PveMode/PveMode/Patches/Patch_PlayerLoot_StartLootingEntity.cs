using HarmonyLib;

namespace PveModeHarmony.Patches
{
    /// <summary>
    /// Blocks looting of event crates / NPC corpses / dropped backpacks by non-owner,
    /// non-team players. Mirrors Oxide PveMode's CanLootEntity family of hooks.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class Patch_PlayerLoot_StartLootingEntity
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            if (targetEntity == null) return true;

            BasePlayer player = __instance != null ? __instance.GetComponentInParent<BasePlayer>() : null;
            if (player == null) return true;

            object result = PveModeManager.CanLootEntity(player, targetEntity);
            if (result is bool blocked && blocked)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
