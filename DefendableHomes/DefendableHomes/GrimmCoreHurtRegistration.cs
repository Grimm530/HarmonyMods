using DHPlugin = Harmony.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "DefendableHomes";
        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(DHPlugin.Dispatch_Hurt(entity, info));
    }
}
