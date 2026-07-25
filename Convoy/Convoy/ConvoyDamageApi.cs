using System;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// TruePVE / inter-mod API: allow player damage to active convoy entities despite
    /// TruePVE rule "nothing can hurt cars", and allow event turrets to target players.
    /// Published via AppDomain for TruePVE CallHook.
    /// </summary>
    public static class ConvoyDamageApi
    {
        public const string AppDomainHandlerKey = "Convoy_CanEntityTakeDamage";
        public const string AppDomainTargetHandlerKey = "Convoy_CanEntityBeTargeted";

        public static void Publish()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainHandlerKey, (Func<BaseEntity, HitInfo, object>)CanEntityTakeDamage);
                AppDomain.CurrentDomain.SetData(AppDomainTargetHandlerKey, (Func<BaseEntity, BaseEntity, object>)CanEntityBeTargeted);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Convoy] Failed to publish damage/target API: " + ex.Message);
            }
        }

        public static void Unpublish()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainHandlerKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainTargetHandlerKey, null);
            }
            catch { }
        }

        /// <summary>
        /// Oxide Convoy CanEntityTakeDamage / CanConvoyVehicleTakeDamage parity.
        /// true = allow (override TruePVE block), false = block, null = not a convoy entity.
        /// </summary>
        public static object CanEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            var ec = EventController.Instance;
            if (ec == null || !ec.IsFullySpawned()) return null;
            if (!ec.IsEventCombatTarget(entity)) return null;

            BasePlayer attacker = info.InitiatorPlayer ?? info.Initiator as BasePlayer;
            if (attacker == null || attacker is NPCPlayer)
                return false;

            return true;
        }

        /// <summary>
        /// Oxide Convoy CanEntityBeTargeted(BasePlayer, AutoTurret) parity for TruePVE.
        /// Args match Interface.CallHook order: [target, attacker/turret].
        /// </summary>
        public static object CanEntityBeTargeted(BaseEntity target, BaseEntity attacker)
        {
            var turret = attacker as AutoTurret;
            var player = target as BasePlayer;
            if (turret == null || player == null || turret.net == null || turret.OwnerID != 0)
                return null;

            var ec = EventController.Instance;
            if (ec == null || !ec.IsFullySpawned()) return null;
            if (!ec.IsEventTurret((ulong)turret.net.ID.Value)) return null;

            if (!player.IsRealPlayer())
                return false;
            if (!ec.IsAggressive())
                return false;

            return true;
        }
    }
}
