using RustEditStandalone.Core;
using RustEditStandalone.Features;

namespace RustEditStandalone
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "RustEditStandalone";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 15, Prefix);
            GrimmCoreBridge.RegisterSpawnPostfix(ModId, 15, Spawn);
            GrimmCoreBridge.RegisterKillObserver(ModId, 15, Patches.BaseNetworkable_Kill_Patch.Prefix);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterSpawnMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }

        private static void Spawn(BaseNetworkable entity) => RustEditHub.NotifyEntitySpawned(entity);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return null;
            if (!DeployableFeature.ShouldBlockDamage(entity) && !IoFeature.IsMapIo(entity))
                return null;

            if (info != null)
            {
                info.damageTypes.ScaleAll(0f);
                info.DidHit = false;
            }
            return true;
        }
    }
}
