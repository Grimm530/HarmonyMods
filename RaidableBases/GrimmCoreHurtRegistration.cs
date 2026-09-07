namespace RaidableBases
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "RaidableBases";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 70, Prefix);
        }

        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            var host = RaidableBasesHost.Instance;
            if (host == null || host.ModInstance == null) return null;

            if (host.IsSubscribed("CanEntityTakeDamage"))
            {
                var can = host.InvokeHookCached("CanEntityTakeDamage", entity, info);
                if (can is bool allowCan && !allowCan) return true;
            }

            if (host.IsSubscribed("OnEntityTakeDamage"))
            {
                var on = host.InvokeHookCached("OnEntityTakeDamage", entity, info);
                if (on is bool allowOn && !allowOn) return true;
            }

            return null;
        }
    }
}
