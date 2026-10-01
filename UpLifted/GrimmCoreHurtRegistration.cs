using UL = Harmony.Plugins.UpLifted;

namespace UpLiftedHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "UpLifted";
        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
            GrimmCoreBridge.RegisterKillObserver(ModId, 110, Kill);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }
        private static void Kill(BaseNetworkable entity)
        {
            try { UL.Dispatch_OnEntityKill(entity); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[UpLifted] OnEntityKill: " + ex.Message); }
        }
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(UL.Dispatch_OnEntityTakeDamage(entity, info));
    }
}
