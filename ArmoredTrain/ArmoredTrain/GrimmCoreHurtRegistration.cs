using ATPlugin = Harmony.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "ArmoredTrain";
        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 110, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
            => GrimmCoreHurtSemantics.BlockIfHandled(ATPlugin.Dispatch_Hurt(entity, info));
    }
}
