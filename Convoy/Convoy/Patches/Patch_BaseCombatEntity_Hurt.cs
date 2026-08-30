using HarmonyLib;
using System.Collections;
using UnityEngine;

namespace Convoy.Patches
{
    /// <summary>
    /// When a player hits any convoy vehicle/module/NPC/crate/turret: stop convoy + dismount NPCs.
    /// Catch hits in Prefix so this still fires if GrimmNPC/TruePVE skips Hurt, but defer the stop one
    /// frame so mounted NPC headshot damage applies before Convoy kills/respawns them on foot.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPrefix]
        public static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            TryScaleBradleyBuildingDamage(__instance, info);

            if (!TryGetConvoyHit(__instance, info, out var ec, out var attacker)) return;

            if (ServerMgr.Instance != null)
                ServerMgr.Instance.StartCoroutine(DelayedStop(ec, attacker));
            else
                ec.OnConvoyAttacked(attacker);
        }

        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (!TryGetConvoyHit(__instance, info, out _, out var attacker)) return;

            // When shared PveMode owns event lockouts, skip Convoy's simple team-damage lock.
            if (PveModeManager.IsPveModeReady()) return;

            if (info.damageTypes == null || info.damageTypes.Total() <= 0f) return;

            ulong teamId = attacker.currentTeam;
            if (teamId == 0) return;

            var mod = ConvoyMod.Instance;
            if (mod?.Config?.LootSettings == null) return;
            float threshold = mod.Config.LootSettings.EventLockDamageThreshold;
            if (threshold <= 0f) return;

            ConvoyState.EnsureLockExpiry(mod.Config.LootSettings.EventLockUnlockAfterSeconds);
            bool justLocked = ConvoyState.RecordDamage(teamId, info.damageTypes.Total(), threshold, out _);
            if (justLocked && mod.Config.Debug)
                UnityEngine.Debug.Log($"[Convoy] Event locked to team {teamId} (threshold {threshold}).");
        }

        private static void TryScaleBradleyBuildingDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is not BuildingBlock) return;
            if (info?.Initiator is not BradleyAPC apc || apc.net == null) return;
            var ec = EventController.Instance;
            if (ec == null || !ec.IsFullySpawned()) return;
            float scale = ec.GetBradleyBuildingDamageScale((ulong)apc.net.ID.Value);
            if (scale < 0f || info.damageTypes == null) return;
            info.damageTypes.ScaleAll(scale);
        }

        private static bool TryGetConvoyHit(BaseCombatEntity entity, HitInfo info, out EventController ec, out BasePlayer attacker)
        {
            ec = null;
            attacker = null;
            if (entity == null || info == null) return false;

            attacker = info.InitiatorPlayer;
            if (attacker == null)
                attacker = info.Initiator as BasePlayer;
            if (attacker == null || attacker is NPCPlayer) return false;

            ec = EventController.Instance;
            if (ec == null || !ec.IsFullySpawned()) return false;
            return ec.IsEventCombatTarget(entity);
        }

        private static IEnumerator DelayedStop(EventController ec, BasePlayer attacker)
        {
            yield return null;
            if (ec == null || !ec.IsFullySpawned()) yield break;
            if (attacker == null || attacker.IsDestroyed) yield break;
            ec.OnConvoyAttacked(attacker);
        }
    }
}
