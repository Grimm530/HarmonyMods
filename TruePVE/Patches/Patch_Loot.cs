// Loot protection: CanLootEntity / CanLootPlayer / OnStartBeingLooted (block via prefix)
// and OnLootEntity / OnLootPlayer notifications (postfix).
// Target: PlayerLoot.StartLootingEntity(BaseEntity, bool). Non-null result cancels the loot.
using HarmonyLib;
using UnityEngine;
using TPVE = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class Patch_PlayerLoot_StartLootingEntity
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            if (__instance == null || targetEntity == null) return true;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return true;

            try
            {
                if (targetEntity is BasePlayer target)
                {
                    if (TPVE.Dispatch_CanLootPlayer(target, player) != null) { __result = false; return false; }
                }
                else
                {
                    if (TPVE.Dispatch_CanLootEntity(player, targetEntity) != null) { __result = false; return false; }
                    if (targetEntity is DroppedItemContainer dic &&
                        TPVE.Dispatch_OnStartBeingLooted(dic, player) != null) { __result = false; return false; }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] CanLoot prefix: " + ex.Message); }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity, bool __result)
        {
            if (!__result || __instance == null || targetEntity == null) return;
            BasePlayer player = __instance.baseEntity;
            if (player == null) return;
            try
            {
                TPVE.Dispatch_OnLootEntity(player, targetEntity);
                if (targetEntity is BasePlayer target) TPVE.Dispatch_OnLootPlayer(target, player);
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnLoot postfix: " + ex.Message); }
        }
    }
}
