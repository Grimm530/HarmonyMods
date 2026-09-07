using VQ = Harmony.Plugins.VirtualQuarries;

namespace VirtualQuarriesHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "VirtualQuarries";
        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(VQ.Dispatch_OnEntityTakeDamage(entity, info));
    }
}
