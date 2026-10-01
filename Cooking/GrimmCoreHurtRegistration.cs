using CookingPlugin = Harmony.Plugins.Cooking;

namespace CookingHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "Cooking";
        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
            GrimmCoreBridge.RegisterKillObserver(ModId, 110, Patches.BaseNetworkable_Kill_Patch.Prefix);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(CookingPlugin.Dispatch_OnEntityTakeDamage(entity, info));
    }
}
