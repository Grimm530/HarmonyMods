using HarmonyLib;
using UnityEngine;
using ZM = Oxide.Plugins.ZoneManager;

namespace ZoneManagerHarmony.Patches
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
                    if (ZM.Dispatch_CanLootPlayer(target, player) != null) { __result = false; return false; }
                }
                else if (ZM.Dispatch_CanLootEntity(player, targetEntity) != null)
                {
                    __result = false;
                    return false;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] CanLoot prefix: " + ex.Message); }
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
                ZM.Dispatch_OnLootEntity(player, targetEntity);
                if (targetEntity is BasePlayer target) ZM.Dispatch_OnLootPlayer(player, target);
            }
            catch (System.Exception ex) { Debug.LogWarning("[ZoneManager] OnLoot postfix: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), "CanCompletePickup")]
    public static class Patch_CanCompletePickup
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, BasePlayer player, ref bool __result)
        {
            object result = ZM.Dispatch_CanPickupEntity(player, __instance);
            if (result is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseLock), nameof(BaseLock.RPC_TakeLock))]
    public static class Patch_CanPickupLock
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseLock __instance, BaseEntity.RPCMessage rpc)
        {
            if (ZM.Dispatch_CanPickupLock(rpc.player, __instance) != null)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(WorldItem), nameof(WorldItem.Pickup))]
    public static class Patch_OnItemPickup
    {
        [HarmonyPrefix]
        public static bool Prefix(WorldItem __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance.item == null || msg.player == null) return true;
            if (ZM.Dispatch_OnItemPickup(__instance.item, msg.player) != null)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(CollectibleEntity), nameof(CollectibleEntity.DoPickup))]
    public static class Patch_OnCollectiblePickup
    {
        [HarmonyPrefix]
        public static bool Prefix(CollectibleEntity __instance, BasePlayer reciever)
        {
            return ZM.Dispatch_OnGather(reciever) == null;
        }
    }

    [HarmonyPatch(typeof(GrowableEntity), nameof(GrowableEntity.PickFruit))]
    public static class Patch_OnGrowableGather
    {
        [HarmonyPrefix]
        public static bool Prefix(GrowableEntity __instance, BasePlayer player)
        {
            return ZM.Dispatch_OnGather(player) == null;
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), "GiveResourceFromItem")]
    public static class Patch_OnDispenserGather
    {
        [HarmonyPrefix]
        public static bool Prefix(ResourceDispenser __instance, BasePlayer entity)
        {
            if (entity == null) return true;
            return ZM.Dispatch_OnGather(entity) == null;
        }
    }
}
