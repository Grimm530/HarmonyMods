using TPVE = Harmony.Plugins.TruePVE;

namespace TruePVEHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "TruePVE";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId + ".Deployable", 0, DeployablePrefix);
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 60, MainPrefix);
        }

        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterHurtMod(ModId + ".Deployable");
        }

        private static bool? DeployablePrefix(BaseCombatEntity entity, HitInfo info)
        {
            // Legacy TruePVE.PvEDamageHelpers stack removed; main TruePVEPlugin damage pipeline owns PvE rules.
            return null;
        }

        private static bool? MainPrefix(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.InitiatorPlayer != null)
                TPVE.Dispatch_OnPlayerAttack(info.InitiatorPlayer, info);
            return GrimmCoreHurtSemantics.BlockIfHandled(TPVE.Dispatch_OnEntityTakeDamage(entity, info));
        }
    }
}
