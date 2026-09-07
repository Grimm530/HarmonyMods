namespace GrimmNPC
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "GrimmNPC";

        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            var ins = GrimmNPC.Instance;
            if (ins == null || entity == null || info == null) return null;
            return GrimmCoreHurtSemantics.BlockIfHandled(ins.OnEntityTakeDamage(entity, info));
        }
    }
}
