// Loot protection: CanLootEntity / CanLootPlayer / OnStartBeingLooted (block via prefix)
// and OnLootEntity / OnLootPlayer notifications (postfix).
// Target: PlayerLoot.StartLootingEntity(BaseEntity, bool).
// CanLootEntity / CanLootPlayer: non-null cancels loot (TruePVE returns true to block).
// OnStartBeingLooted: true = force-allow (bypass onlyOwnerLoot), false = block, null = vanilla.
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
                    if (targetEntity is DroppedItemContainer dic)
                    {
                        object started = TPVE.Dispatch_OnStartBeingLooted(dic, player);
                        // true = TruePVE force-allowed (unowned bags / onlyOwnerLoot bypass).
                        // Clear onlyOwnerLoot so vanilla DroppedItemContainer.OnStartBeingLooted
                        // does not immediately re-block (playerSteamID==0 + onlyOwnerLoot blocks everyone).
                        if (started is true)
                        {
                            dic.onlyOwnerLoot = false;
                            return true;
                        }
                        if (started is false)
                        {
                            __result = false;
                            return false;
                        }
                    }
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
