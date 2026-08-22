using System;
using OxidePlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes
{
    /// <summary>
    /// TruePVE / inter-mod API: event GrimmNPCs may damage the defended base, and base auto turrets
    /// may target those NPCs. Published via AppDomain for TruePVE CallHook — same pattern as Convoy / ArmoredTrain.
    /// </summary>
    public static class DefendableHomesDamageApi
    {
        public const string AppDomainHandlerKey = "DefendableHomes_CanEntityTakeDamage";
        public const string AppDomainTargetHandlerKey = "DefendableHomes_CanEntityBeTargeted";

        public static void Publish()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainHandlerKey, (Func<BaseEntity, HitInfo, object>)CanEntityTakeDamage);
                AppDomain.CurrentDomain.SetData(AppDomainTargetHandlerKey, (Func<BaseEntity, BaseEntity, object>)CanEntityBeTargeted);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[DefendableHomes] Failed to publish damage/target API: " + ex.Message);
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

        public static object CanEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            return OxidePlugin.Dispatch_CanEntityTakeDamage(entity, info);
        }

        /// <summary>Args match Interface.CallHook order: [target, attacker/turret].</summary>
        public static object CanEntityBeTargeted(BaseEntity target, BaseEntity attacker)
        {
            return OxidePlugin.Dispatch_CanEntityBeTargeted(target, attacker);
        }
    }
}
