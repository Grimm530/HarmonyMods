using CCPlugin = Harmony.Plugins.CombatClasses;

namespace CombatClassesHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "CombatClasses";
        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(CCPlugin.Dispatch_OnEntityTakeDamage(entity, info));
    }
}
