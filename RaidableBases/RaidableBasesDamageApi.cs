using System;
using UnityEngine;

namespace RaidableBases
{
    /// <summary>
    /// TruePVE / inter-mod API: allow damage and targeting inside active raid bases.
    /// TruePVE has no Harmony hook bus; it only consults AppDomain delegates for
    /// CanEntityTakeDamage / CanEntityBeTargeted (same pattern as Convoy / PveMode).
    /// Without this, TruePVE's defaultAllowDamage=false blocks all raid building damage.
    /// </summary>
    public static class RaidableBasesDamageApi
    {
        public const string AppDomainHandlerKey = "RaidableBases_CanEntityTakeDamage";
        public const string AppDomainTargetHandlerKey = "RaidableBases_CanEntityBeTargeted";

        public static void Publish()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainHandlerKey, (Func<BaseEntity, HitInfo, object>)CanEntityTakeDamage);
                AppDomain.CurrentDomain.SetData(AppDomainTargetHandlerKey, (Func<BaseEntity, BaseEntity, object>)CanEntityBeTargeted);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBases] Failed to publish damage/target API: " + ex.Message);
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
        /// true = allow (override TruePVE block), false = block, null = not raid-related.
        /// </summary>
        public static object CanEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
                return null;
            if (entity is not BaseCombatEntity combat)
                return null;

            // TruePVE CallHook only accepts bool. Always invoke the object-returning
            // CanEntityTakeDamage method — never void OnEntityTakeDamage (Method.Invoke → null
            // → RuleSets block OwnerID-0 raid buildings while player bases stay protected).
            var host = RaidableBasesHost.Instance;
            if (host == null || host.ModInstance == null)
                return null;

            if (!host.IsSubscribed("CanEntityTakeDamage") && !host.IsSubscribed("OnEntityTakeDamage"))
                return null;

            return host.InvokeCanEntityTakeDamage(combat, info);
        }

        /// <summary>
        /// Args match TruePVE CallHook order: [target, attacker].
        /// </summary>
        public static object CanEntityBeTargeted(BaseEntity target, BaseEntity attacker)
        {
            if (target == null || attacker == null)
                return null;

            var host = RaidableBasesHost.Instance;
            if (host == null || host.ModInstance == null || !host.IsSubscribed("CanEntityBeTargeted"))
                return null;

            if (target is BasePlayer player)
                return host.InvokeHook("CanEntityBeTargeted", new object[] { player, attacker });

            return host.InvokeHook("CanEntityBeTargeted", new object[] { target, attacker });
        }
    }
}
