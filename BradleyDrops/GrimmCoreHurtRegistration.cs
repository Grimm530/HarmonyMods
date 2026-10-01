using BD = Harmony.Plugins.BradleyDrops;

namespace BradleyDropsHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "BradleyDrops";
        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 100, Prefix);
            GrimmCoreBridge.RegisterSpawnPostfix(ModId, 100, Spawn);
            GrimmCoreBridge.RegisterKillObserver(ModId, 100, Kill);
        }
        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterSpawnMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }
        private static void Spawn(BaseNetworkable entity)
        {
            if (entity is BasePlayer) return;
            BD.Dispatch_OnEntitySpawned(entity);
        }
        private static void Kill(BaseNetworkable entity)
        {
            try { BD.Dispatch_OnEntityKill(entity); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[BradleyDrops] OnEntityDestroy: " + ex.Message); }
        }
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
