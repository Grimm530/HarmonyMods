using STPlugin = Harmony.Plugins.SkillTree;

namespace SkillTreeHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "SkillTree";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
            GrimmCoreBridge.RegisterSpawnPostfix(ModId, 110, Spawn);
            GrimmCoreBridge.RegisterKillObserver(ModId, 110, Patches.BaseNetworkable_Kill_Patch.Prefix);
        }

        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterSpawnMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }

        private static void Spawn(BaseNetworkable entity)
        {
            if (entity is BasePlayer) return;
            STPlugin.Dispatch_OnEntitySpawned(entity);
        }

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is PatrolHelicopter heli)
                STPlugin.Dispatch_OnPatrolHelicopterTakeDamage(heli, info);
            return GrimmCoreHurtSemantics.BlockIfHandled(STPlugin.Dispatch_OnEntityTakeDamage(entity, info));
        }
    }
}
