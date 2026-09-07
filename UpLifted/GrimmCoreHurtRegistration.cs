using UL = Harmony.Plugins.UpLifted;

namespace UpLiftedHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "UpLifted";
        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(UL.Dispatch_OnEntityTakeDamage(entity, info));
    }
}
