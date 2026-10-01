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

            if (!host.IsSubscribed("CanEntityTakeDamage") && !host.IsSubscribed("OnEntityTakeDamage"))
                return null;

            // Always use CanEntityTakeDamage (returns bool). TruePVE may already have allowed
            // OwnerID-0 raid hits via AppDomain; we only block when RB says false.
            var can = host.InvokeCanEntityTakeDamage(entity, info);
            if (can is bool allow && !allow) return true;

            return null;
        }
    }
}
