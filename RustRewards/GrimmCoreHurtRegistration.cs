using UnityEngine;
using RustRewardsHarmony.Patches;

namespace GrimmRewards.Patches
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "RustRewards";

        internal static void Register() => GrimmCoreBridge.RegisterHurtPostfix(ModId, 141, Postfix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static void Postfix(BaseCombatEntity entity, HitInfo info)
        {
            try
            {
                if (entity == null || info == null) return;
                RewardHooks.Plugin?.OnEntityTakeDamage(entity, info);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[GrimmRewards] OnEntityTakeDamage: " + ex.Message);
            }
        }
    }
}
