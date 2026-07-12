using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// When the event is locked to a team, only that team can loot convoy entities (crates, NPC corpses, etc.).
    /// If no event lock, no Convoy restriction (TruePVE/Loot Defender handle protection when present).
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class Patch_PlayerLoot_StartLootingEntity
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            if (targetEntity?.net == null) return true;
            ulong netId = (ulong)targetEntity.net.ID.Value;
            if (!ConvoyState.IsConvoyEntity(netId)) return true;

            var mod = ConvoyMod.Instance;
            if (mod?.Config?.LootSettings != null)
                ConvoyState.EnsureLockExpiry(mod.Config.LootSettings.EventLockUnlockAfterSeconds);

            if (ConvoyState.LockedTeamId == 0) return true;

            BasePlayer player = __instance?.GetComponentInParent<BasePlayer>();
            if (player == null) return true;
            if (ConvoyState.IsLockedToPlayerTeam(player)) return true;

            __result = false;
            return false;
        }
    }
}
