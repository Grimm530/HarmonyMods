using GBPlugin = Harmony.Plugins.GrimmBoss;

namespace GrimmBoss.Patches
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "GrimmBoss";
        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtSideEffect(ModId + ".Barricade", 50, SideEffect);
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterHurtMod(ModId + ".Barricade");
        }
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(GBPlugin.Dispatch_Hurt(entity, info));
        /// <summary>Always runs before block prefixes — barricade placement must not depend on damage applying.</summary>
        private static void SideEffect(BaseCombatEntity entity, HitInfo info)
            => GBPlugin.Dispatch_HurtSideEffect(entity, info);
    }
}
