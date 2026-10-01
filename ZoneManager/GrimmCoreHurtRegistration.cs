using ZM = Harmony.Plugins.ZoneManager;

namespace ZoneManagerHarmony
{
    internal static class GrimmCoreHurtRegistration
    {
        private const string ModId = "ZoneManager";

        internal static void Register()
        {
            GrimmCoreBridge.RegisterHurtPrefix(ModId, 80, Prefix);
            GrimmCoreBridge.RegisterSpawnPostfix(ModId, 80, Spawn);
            GrimmCoreBridge.RegisterKillObserver(ModId, 80, Kill);
        }

        internal static void Unregister()
        {
            GrimmCoreBridge.UnregisterHurtMod(ModId);
            GrimmCoreBridge.UnregisterSpawnMod(ModId);
            GrimmCoreBridge.UnregisterGameHookMod(ModId);
        }

        private static void Spawn(BaseNetworkable entity)
        {
            if (!(entity is BaseEntity be)) return;
            if (ZoneManagerHarmony.Patches.Deployer_DoDeploy_Patch.Pending != null)
            {
                var deployer = ZoneManagerHarmony.Patches.Deployer_DoDeploy_Patch.Pending;
                var item = deployer.GetItem();
                var mod = item?.info?.GetComponent<ItemModDeployable>();
                if (mod != null)
                    ZM.Dispatch_OnItemDeployed(deployer, mod, be);
                else
                    ZM.Dispatch_OnItemDeployed(deployer, deployer.GetParentEntity(), be);
            }
            ZM.Dispatch_OnEntitySpawned(be);
        }

        private static void Kill(BaseNetworkable entity)
        {
            if (!(entity is BaseEntity be)) return;
            try { ZM.Dispatch_OnEntityKill(be); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[ZoneManager] OnEntityKill: " + ex.Message); }
        }

        private static bool? Prefix(BaseCombatEntity entity, HitInfo info)
        {
            return GrimmCoreHurtSemantics.BlockIfHandled(ZM.Dispatch_OnEntityTakeDamage(entity, info));
        }
    }
}
