using HarmonyLib;
using ATPlugin = Oxide.Plugins.ArmoredTrain;

namespace ArmoredTrain.Patches
{
    /// <summary>
    /// Port of ArmoredTrain CanLootEntity(...) (block) + OnLootEntity(...) (post) hooks. The prefix
    /// enforces loot protection / triggers aggression; the postfix registers event-crate looting.
    /// </summary>
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class Patch_PlayerLoot_StartLootingEntity
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            BasePlayer player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return true;

            object result = ATPlugin.Dispatch_CanLoot(player, targetEntity);
            if (result != null)
            {
                __result = false;
                return false; // block looting
            }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result) return;
            BasePlayer player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return;
            ATPlugin.Dispatch_OnLootEntity(player, targetEntity);
        }
    }
}
