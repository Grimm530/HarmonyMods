namespace RustLeagueHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "RustLeague";

        internal static void Register() => GrimmCoreBridge.RegisterHurtSideEffect(ModId, 130, SideEffect);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static void SideEffect(BaseCombatEntity entity, HitInfo info)
        {
            var plugin = RustLeagueMod.Instance?.Plugin;
            if (plugin == null || entity == null || info == null) return;
            plugin.TryNegateEventDamage(entity, info);
        }
    }
}
