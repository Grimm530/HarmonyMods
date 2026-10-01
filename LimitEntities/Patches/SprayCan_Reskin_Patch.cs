using HarmonyLib;

namespace LimitEntities.Patches
{
    /// <summary>
    /// Oxide OnEntityReskinned (Limit Entities 2.3.9).
    /// SprayCan redirect swaps Kill the old entity, then Spawn the new one before Reskin_Restore
    /// writes OwnerID, so the spawn hook skips it. This runs after that restore, for both
    /// redirect swaps and skin-id-only changes, with the final entity.
    /// </summary>
    [HarmonyPatch(typeof(Facepunch.Rust.Analytics.Azure), nameof(Facepunch.Rust.Analytics.Azure.OnEntitySkinChanged), typeof(BasePlayer), typeof(BaseNetworkable), typeof(int))]
    internal static class Analytics_OnEntitySkinChanged_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(BaseNetworkable entity)
        {
            if (entity is not BaseEntity baseEntity) return;
            var service = LimitEntitiesMod.Service;
            if (service == null || !service.IsReady) return;
            service.OnEntityReskinned(baseEntity);
        }
    }
}
