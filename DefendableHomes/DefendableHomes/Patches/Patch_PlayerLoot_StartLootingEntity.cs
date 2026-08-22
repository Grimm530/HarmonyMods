using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class Patch_PlayerLoot_StartLootingEntity
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            BasePlayer player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return true;

            object result = DHPlugin.Dispatch_CanLoot(player, targetEntity);
            if (result != null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
