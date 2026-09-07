namespace PersonalNPCHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "PersonalNPC";

        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 90, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            try
            {
                if (PersonalNPCHarmony.Patches.Hooks.Core?.OnEntityTakeDamage(entity, info) != null) return true;
            }
            catch (System.Exception ex) { PersonalNPCHarmony.Patches.Hooks.Warn("OnEntityTakeDamage", ex); }
            try
            {
                if (PersonalNPCHarmony.Patches.Hooks.Builder?.OnEntityTakeDamage(entity, info) != null) return true;
            }
            catch (System.Exception ex) { PersonalNPCHarmony.Patches.Hooks.Warn("Builder OnEntityTakeDamage", ex); }
            return null;
        }
    }
}
