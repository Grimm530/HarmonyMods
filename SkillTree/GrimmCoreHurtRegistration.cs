using STPlugin = Harmony.Plugins.SkillTree;

namespace SkillTreeHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "SkillTree";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        }

        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is PatrolHelicopter heli)
                STPlugin.Dispatch_OnPatrolHelicopterTakeDamage(heli, info);
            return GrimmCoreHurtSemantics.BlockIfHandled(STPlugin.Dispatch_OnEntityTakeDamage(entity, info));
        }
    }
}
