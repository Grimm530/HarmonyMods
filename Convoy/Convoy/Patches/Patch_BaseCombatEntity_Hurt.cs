using HarmonyLib;
using UnityEngine;

namespace Convoy.Patches
{
    /// <summary>
    /// When a player damages any convoy vehicle/module/NPC/crate/turret: stop convoy + dismount NPCs.
    /// </summary>
    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Hurt), new[] { typeof(HitInfo) })]
    public static class Patch_BaseCombatEntity_Hurt
    {
        [HarmonyPostfix]
        public static void Postfix(BaseCombatEntity __instance, HitInfo info)
        {
            if (__instance == null || info?.damageTypes == null) return;
            BasePlayer attacker = info.InitiatorPlayer;
            if (attacker == null)
                attacker = info.Initiator as BasePlayer;
            if (attacker == null || attacker is NPCPlayer) return;
            if (info.damageTypes.Total() <= 0f) return;

            var ec = EventController.Instance;
            if (ec == null || !ec.IsFullySpawned()) return;
            if (!ec.IsEventCombatTarget(__instance)) return;

            ec.OnConvoyAttacked(attacker);

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
    }
}
