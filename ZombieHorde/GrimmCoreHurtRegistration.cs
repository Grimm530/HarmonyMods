using Rust;

namespace ZombieHorde
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "ZombieHorde";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 120, Prefix);
        }

        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            if (ConfigData.Configuration == null || info == null || entity == null)
                return null;

            // GunTrap/FlameTurret: no CanBeTargeted hook — block trap damage when config forbids.
            if (info.Initiator is GunTrap or FlameTurret)
            {
                if (ZombieNPC.Get(entity as BasePlayer) != null)
                {
                    object trapResult = ZombieHordePlugin.Instance?.CanBeTargeted(entity, info.Initiator);
                    if (trapResult is bool b && !b)
                        return true;
                }
            }

            ZombieNPC victim = ZombieNPC.Get(entity as BasePlayer);
            if (victim != null)
            {
                BasePlayer initiator = info.InitiatorPlayer;
                if (initiator != null && initiator.IsNpc && ZombieNPC.Get(initiator) == null
                    && !ConfigData.Configuration.Member.TargetedByNPCs)
                    return true;

                if (info.Initiator is BaseNpc && !(info.Initiator is BasePlayer)
                    && !ConfigData.Configuration.Member.TargetedByAnimals)
                    return true;
            }

            ZombieHordePlugin.Instance?.OnEntityTakeDamage(entity, info);
            return null;
        }
    }
}
