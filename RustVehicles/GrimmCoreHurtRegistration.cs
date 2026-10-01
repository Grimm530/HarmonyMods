namespace RustVehiclesHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "RustVehicles";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtSideEffect(ModId, 131, SideEffect);
            GrimmCoreBridge.RegisterSpawnPostfix(ModId, 131, Spawn);
            GrimmCoreBridge.RegisterKillObserver(ModId, 131, Patches.BaseNetworkable_Kill_Patch.Prefix);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterSpawnMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }

        private static void Spawn(BaseNetworkable entity)
        {
            var plugin = RustVehiclesHarmony.Patches.Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntitySpawned")) return;
            try
            {
                switch (entity)
                {
                    case Tugboat tug: plugin.OnEntitySpawned(tug); break;
                    case BaseSubmarine sub: plugin.OnEntitySpawned(sub); break;
                    case MotorRowboat row: plugin.OnEntitySpawned(row); break;
                    case Minicopter mini: plugin.OnEntitySpawned(mini); break;
                    case AttackHelicopter atk: plugin.OnEntitySpawned(atk); break;
                }
            }
            catch (System.Exception ex) { RustVehiclesHarmony.Patches.Hooks.Warn("OnEntitySpawned", ex); }
        }

        private static void SideEffect(BaseCombatEntity entity, HitInfo info)
        {
            var plugin = RustVehiclesHarmony.Patches.Hooks.Plugin;
            if (plugin == null || !plugin.IsSubscribed("OnEntityTakeDamage")) return;
            if (entity == null || info == null) return;
            try { plugin.OnEntityTakeDamage(entity, info); }
            catch (System.Exception ex) { RustVehiclesHarmony.Patches.Hooks.Warn("OnEntityTakeDamage", ex); }
        }
    }
}
