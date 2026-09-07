using ZM = Harmony.Plugins.ZoneManager;

namespace ZoneManagerHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "ZoneManager";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 80, Prefix);
        }

        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            return GrimmCoreHurtSemantics.BlockIfHandled(ZM.Dispatch_OnEntityTakeDamage(entity, info));
        }
    }
}
