namespace Convoy.Patches
{
    using Rust;

    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "Convoy";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 55, SuicidePrefix);
            GrimmCoreBridge.RegisterHurtPostfix(ModId, 140, Postfix);
        }

        internal static void Unregister() => GrimmCoreBridge.UnregisterHurtMod(ModId);

        /// <summary>Block invalid-dismount Suicide kills on convoy NPCs (was a separate Hurt patch).</summary>
        private static bool? SuicidePrefix(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.damageTypes == null) return null;
            if (info.damageTypes.Get(DamageType.Suicide) <= 0f) return null;
            if (!ConvoyDismountGuard.IsConvoyNpc(entity as BasePlayer)) return null;
            return true;
        }

        private static void Postfix(BaseCombatEntity entity, HitInfo info)
        {
            if (entity?.net == null || info?.damageTypes == null) return;
            BasePlayer attacker = info.InitiatorPlayer;
            if (attacker == null) return;
            ulong teamId = attacker.currentTeam;
            if (teamId == 0) return;
            float amount = info.damageTypes.Total();
            if (amount <= 0f) return;

            ulong netId = (ulong)entity.net.ID.Value;
            if (!ConvoyState.IsConvoyEntity(netId)) return;

            var mod = ConvoyMod.Instance;
            if (mod?.Config?.LootSettings == null) return;
            float threshold = mod.Config.LootSettings.EventLockDamageThreshold;
            if (threshold <= 0f) return;

            ConvoyState.EnsureLockExpiry(mod.Config.LootSettings.EventLockUnlockAfterSeconds);
            bool justLocked = ConvoyState.RecordDamage(teamId, amount, threshold, out _);
            if (justLocked && mod.Config.Debug)
                UnityEngine.Debug.Log($"[Convoy] Event locked to team {teamId} after {amount} damage (threshold {threshold}).");
        }
    }
}
