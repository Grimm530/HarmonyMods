namespace PveModeHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "PveMode";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 50, Prefix);
        }

        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            object result = PveModeManager.OnEntityTakeDamage(entity, info);
            return result is bool blocked && blocked ? true : (bool?)null;
        }
    }
}
