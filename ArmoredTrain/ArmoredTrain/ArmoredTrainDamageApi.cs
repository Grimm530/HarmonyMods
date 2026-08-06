using System;
using OxidePlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain
{
    /// <summary>
    /// TruePVE / inter-mod API: allow player damage to ArmoredTrain event entities despite
    /// TruePVE rule "players cannot hurt traps" (AutoTurret), and allow event turrets to
    /// target players. Published via AppDomain for TruePVE CallHook — same pattern as Convoy.
    /// </summary>
    public static class ArmoredTrainDamageApi
    {
        public const string AppDomainHandlerKey = "ArmoredTrain_CanEntityTakeDamage";
        public const string AppDomainTargetHandlerKey = "ArmoredTrain_CanEntityBeTargeted";

        public static void Publish()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainHandlerKey, (Func<BaseEntity, HitInfo, object>)CanEntityTakeDamage);
                AppDomain.CurrentDomain.SetData(AppDomainTargetHandlerKey, (Func<BaseEntity, BaseEntity, object>)CanEntityBeTargeted);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ArmoredTrain] Failed to publish damage/target API: " + ex.Message);
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
        /// true = allow (override TruePVE block), false = block, null = not an ArmoredTrain entity.
        /// </summary>
        public static object CanEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            return OxidePlugin.Dispatch_CanEntityTakeDamage(entity, info);
        }

        /// <summary>
        /// Args match Interface.CallHook order: [target, attacker/turret].
        /// </summary>
        public static object CanEntityBeTargeted(BaseEntity target, BaseEntity attacker)
        {
            return OxidePlugin.Dispatch_CanEntityBeTargeted(target, attacker);
        }
    }
}
