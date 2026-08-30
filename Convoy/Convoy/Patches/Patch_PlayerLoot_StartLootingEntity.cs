using HarmonyLib;

namespace Convoy.Patches
{
    /// <summary>
    /// Loot gates: PveMode owner rules when enabled; otherwise Convoy LockedTeamId fallback.
    /// Also applies LootConfig moving/NPC/Bradley/heli bans via ShouldBlockLootingConvoyCrate.
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

            BasePlayer player = __instance?.GetComponentInParent<BasePlayer>();
            if (player == null) return true;

            var mod = ConvoyMod.Instance;
            if (mod != null && mod.ShouldBlockLootingConvoyCrate(targetEntity))
            {
                __result = false;
                return false;
            }

            if (PveModeManager.IsPveModeReady())
            {
                if (PveModeManager.IsPveModeBlockInteractByCooldown(player)
                    || PveModeManager.IsPveModeBlockNoOwnerLooting(player)
                    || PveModeManager.IsPveModDefaultBlockAction(player))
                {
                    __result = false;
                    return false;
                }
                // PveMode's own loot patch also enforces crate/corpse rules; do not use LockedTeamId.
                return true;
            }

            if (mod?.Config?.LootSettings != null)
                ConvoyState.EnsureLockExpiry(mod.Config.LootSettings.EventLockUnlockAfterSeconds);

            if (ConvoyState.LockedTeamId == 0) return true;
            if (ConvoyState.IsLockedToPlayerTeam(player)) return true;

            __result = false;
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(BaseEntity targetEntity, bool __runOriginal)
        {
            if (!__runOriginal || targetEntity?.net == null) return;
            if (!ConvoyState.IsConvoyCrate((ulong)targetEntity.net.ID.Value)) return;
            EventController.Instance?.OnEventCrateLooted(targetEntity);
        }
    }
}
