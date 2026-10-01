using DHPlugin = Harmony.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "DefendableHomes";
        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
            GrimmCoreBridge.RegisterSpawnPostfix(ModId, 110, Spawn);
            GrimmCoreBridge.RegisterKillObserver(ModId, 110, Kill);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterSpawnMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }
        private static void Spawn(BaseNetworkable entity) => DHPlugin.Dispatch_Spawned(entity);
        private static void Kill(BaseNetworkable entity) => DHPlugin.Dispatch_OnEntityKill(entity);
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(DHPlugin.Dispatch_Hurt(entity, info));
    }
}
