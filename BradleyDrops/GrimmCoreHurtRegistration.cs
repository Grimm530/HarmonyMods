using BD = Harmony.Plugins.BradleyDrops;

namespace BradleyDropsHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "BradleyDrops";
        internal static void Register() => GrimmCoreBridge.RegisterHurtPrefix(ModId, 100, Prefix);
        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);
        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.InitiatorPlayer != null)
            {
                var attack = BD.Dispatch_OnPlayerAttack(info.InitiatorPlayer, info);
                if (attack != null) return true;
            }
            return BD.Dispatch_OnEntityTakeDamage(entity, info) == null ? (bool?)null : true;
        }
    }
}
