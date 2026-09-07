using RustEditStandalone.Features;

namespace RustEditStandalone
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "RustEditStandalone";

        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 15, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

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
