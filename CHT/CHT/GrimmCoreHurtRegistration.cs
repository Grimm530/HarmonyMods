namespace CHT.Patches
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "CHT";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtSideEffect(ModId, 132, SideEffect);
            GrimmCoreBridge.RegisterSpawnPostfix(ModId, 132, Spawn);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterSpawnMod(ModId);
        }

        private static void Spawn(BaseNetworkable entity)
        {
            if (entity is TimedExplosive explosive)
                CHTMod.Plugin?.OnEntitySpawned(explosive);
        }

        private static void SideEffect(BaseCombatEntity entity, HitInfo info)
        {
            var plugin = CHTMod.Plugin;
            if (plugin == null || info == null) return;
            if (entity is PatrolHelicopter heli)
                plugin.OnPatrolHelicopterTakeDamage(heli, info);
            plugin.OnEntityTakeDamage(entity, info);
        }
    }
}
