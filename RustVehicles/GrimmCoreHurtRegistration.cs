namespace RustVehiclesHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "RustVehicles";

        internal static void Register() => GrimmCoreBridge.RegisterHurtSideEffect(ModId, 131, SideEffect);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

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
