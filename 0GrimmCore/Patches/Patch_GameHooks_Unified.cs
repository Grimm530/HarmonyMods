using System.Collections.Generic;
using HarmonyLib;
using Network;
using ProtoBuf;
using UnityEngine;

namespace GrimmCoreHarmony.Patches
{
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Insert), new[] { typeof(Item), typeof(BasePlayer) })]
    internal static class Patch_ItemContainer_Insert
    {
        [HarmonyPostfix]
        private static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result) return;
            GrimmCoreGameHooks.InvokeItemAdded(__instance, item);
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.RemoveFromContainer))]
    internal static class Patch_Item_RemoveFromContainer
    {
        [HarmonyPrefix]
        private static void Prefix(Item __instance, out ItemContainer __state) => __state = __instance?.parent;

        [HarmonyPostfix]
        private static void Postfix(Item __instance, ItemContainer __state)
        {
            if (__state == null) return;
            GrimmCoreGameHooks.InvokeItemRemoved(__state, __instance);
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer))]
    internal static class Patch_Item_MoveToContainer
    {
        [HarmonyPrefix]
        private static bool Prefix(Item __instance, ItemContainer newcontainer, int iTargetPos, BasePlayer sourcePlayer)
        {
            if (sourcePlayer?.inventory == null) return true;
            var blocked = GrimmCoreGameHooks.InvokeCanMoveItem(
                __instance, sourcePlayer.inventory, newcontainer?.uid ?? default, iTargetPos, __instance?.amount ?? 0);
            return blocked == null;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    internal static class Patch_BasePlayer_Die
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, HitInfo info)
            => GrimmCoreGameHooks.InvokePlayerDeath(__instance, info);
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Kick), new[] { typeof(string), typeof(bool) })]
    internal static class Patch_BasePlayer_Kick
    {
        [HarmonyPrefix]
        private static void Prefix(BasePlayer __instance, string reason)
            => GrimmCoreGameHooks.InvokePlayerKicked(__instance, reason);
    }

    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.SpawnLoot))]
    internal static class Patch_LootContainer_SpawnLoot
    {
        [HarmonyPostfix]
        private static void Postfix(LootContainer __instance)
            => GrimmCoreGameHooks.InvokeLootSpawn(__instance);
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    internal static class Patch_PlayerLoot_StartLootingEntity
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            var player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return true;
            if (GrimmCoreGameHooks.InvokeCanLootEntity(player, targetEntity) == null) return true;
            __result = false;
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerLoot __instance, BaseEntity targetEntity)
        {
            var player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return;
            GrimmCoreGameHooks.InvokeLootEntity(player, targetEntity);
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    internal static class Patch_PlayerLoot_Clear
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerLoot __instance, out BaseEntity __state)
        {
            __state = __instance?.entitySource;
            GrimmCoreGameHooks.InvokePlayerLootEnd(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerLoot __instance, BaseEntity __state)
        {
            var player = __instance?.baseEntity;
            if (player == null || __state == null) return;
            GrimmCoreGameHooks.InvokeLootEntityEnd(player, __state);
        }
    }

    [HarmonyPatch(typeof(SamSite), "AddTargetSet")]
    internal static class Patch_SamSite_AddTargetSet
    {
        [HarmonyPostfix]
        private static void Postfix(SamSite __instance, List<SamSite.ISamSiteTarget> allTargets)
            => GrimmCoreGameHooks.InvokeSamSiteTargetScan(__instance, allTargets);
    }

    [HarmonyPatch(typeof(TimedExplosive), nameof(TimedExplosive.CanStickTo))]
    internal static class Patch_TimedExplosive_CanStickTo
    {
        [HarmonyPrefix]
        private static bool Prefix(TimedExplosive __instance, BaseEntity entity, ref bool __result)
        {
            var r = GrimmCoreGameHooks.InvokeCanExplosiveStick(__instance, entity);
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    internal static class Patch_BasePlayer_PlayerInit
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
            => GrimmCoreGameHooks.InvokePlayerConnected(__instance);
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    internal static class Patch_BasePlayer_OnDisconnected
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
            => GrimmCoreGameHooks.InvokePlayerDisconnected(__instance, null);
    }

    [HarmonyPatch(typeof(BasePlayer), "UpdateActiveItem", new[] { typeof(ItemId) })]
    internal static class Patch_BasePlayer_UpdateActiveItem
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance)
            => GrimmCoreGameHooks.InvokeItemHeld(__instance?.GetActiveItem(), __instance);
    }

    [HarmonyPatch(typeof(BaseLauncher), nameof(BaseLauncher.ProjectileLaunched_Server))]
    internal static class Patch_BaseLauncher_ProjectileLaunched_Server
    {
        [HarmonyPostfix]
        private static void Postfix(BaseLauncher __instance, ServerProjectile justLaunched)
        {
            var player = __instance?.GetOwnerPlayer();
            var entity = justLaunched?.baseEntity as BaseEntity;
            if (player == null || entity == null) return;
            GrimmCoreGameHooks.InvokeRocketLaunched(player, entity);
        }
    }

    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    internal static class Patch_Planner_DoBuild
    {
        [HarmonyPrefix]
        private static bool Prefix(Planner __instance, Construction.Target target, Construction component)
            => GrimmCoreGameHooks.InvokeCanBuild(__instance, component, target) == null;

        [HarmonyPostfix]
        private static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__result == null) return;
            GrimmCoreGameHooks.InvokeEntityBuilt(__instance, __result.gameObject);
        }
    }

    [HarmonyPatch(typeof(Planner), nameof(Planner.PayForPlacement))]
    internal static class Patch_Planner_PayForPlacement
    {
        [HarmonyPrefix]
        private static bool Prefix(Planner __instance, BasePlayer player, Construction component)
            => GrimmCoreGameHooks.InvokePayForPlacement(player, __instance, component) == null;
    }

    [HarmonyPatch(typeof(BaseCombatEntity), "CanCompletePickup")]
    internal static class Patch_BaseCombatEntity_CanCompletePickup
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseCombatEntity __instance, BasePlayer player, ref bool __result)
        {
            if (GrimmCoreGameHooks.InvokeCanPickupEntity(player, __instance) == null) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    internal static class Patch_BaseNetworkable_Kill_Unified
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseNetworkable __instance)
            => GrimmCoreGameHooks.InvokeEntityKill(__instance);
    }

    [HarmonyPatch(typeof(BaseMelee), nameof(BaseMelee.DoAttackShared))]
    internal static class Patch_BaseMelee_DoAttackShared
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseMelee __instance, HitInfo info)
        {
            if (__instance is Hammer) return true;
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return true;
            return GrimmCoreGameHooks.InvokeHammerHit(player, info) == null;
        }
    }

    [HarmonyPatch(typeof(Hammer), nameof(Hammer.DoAttackShared))]
    internal static class Patch_Hammer_DoAttackShared
    {
        [HarmonyPrefix]
        private static bool Prefix(Hammer __instance, HitInfo info)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null || info == null) return true;
            return GrimmCoreGameHooks.InvokeHammerHit(player, info) == null;
        }
    }

    [HarmonyPatch(typeof(Recycler), "SVSwitch")]
    internal static class Patch_Recycler_SVSwitch
    {
        [HarmonyPrefix]
        private static void Prefix(Recycler __instance, BaseEntity.RPCMessage msg)
            => GrimmCoreGameHooks.InvokeRecyclerToggle(__instance, msg.player);
    }

    [HarmonyPatch(typeof(Deployer), "DoDeploy_Regular")]
    internal static class Patch_Deployer_DoDeploy_Regular
    {
        [HarmonyPostfix]
        private static void Postfix(Deployer __instance)
            => GrimmCoreGameHooks.InvokeItemDeployed(__instance);
    }

    [HarmonyPatch(typeof(BaseProjectile), nameof(BaseProjectile.TryReloadMagazine))]
    internal static class Patch_BaseProjectile_TryReloadMagazine
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseProjectile __instance, ref bool __result)
        {
            var r = GrimmCoreGameHooks.InvokeMagazineReload(__instance, 0, __instance.GetOwnerPlayer());
            if (r is bool b)
            {
                __result = b;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.LoseCondition))]
    internal static class Patch_Item_LoseCondition
    {
        [HarmonyPrefix]
        private static void Prefix(Item __instance, float amount)
            => GrimmCoreGameHooks.InvokeLoseCondition(__instance, amount);
    }

    [HarmonyPatch(typeof(BasePlayer), "OnReceiveTick", new[] { typeof(PlayerTick), typeof(bool) })]
    internal static class Patch_BasePlayer_OnReceiveTick
    {
        [HarmonyPostfix]
        private static void Postfix(BasePlayer __instance, PlayerTick msg, bool wasPlayerStalled)
            => GrimmCoreGameHooks.InvokePlayerTick(__instance, msg, wasPlayerStalled);
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.DoRepair))]
    internal static class Patch_BaseCombatEntity_DoRepair
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseCombatEntity __instance, BasePlayer player)
            => GrimmCoreGameHooks.InvokeStructureRepair(__instance, player) == null;
    }

    [HarmonyPatch(typeof(DestroyOnGroundMissing), "OnGroundMissing")]
    internal static class Patch_DestroyOnGroundMissing_OnGroundMissing
    {
        [HarmonyPrefix]
        private static bool Prefix(DestroyOnGroundMissing __instance)
        {
            var entity = GameObjectEx.ToBaseEntity(__instance.gameObject);
            if (entity == null) return true;
            return GrimmCoreGameHooks.InvokeEntityGroundMissing(entity) == null;
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    internal static class Patch_BaseCombatEntity_Die
    {
        [HarmonyPrefix]
        private static void Prefix(BaseCombatEntity __instance, HitInfo info)
            => GrimmCoreGameHooks.InvokeEntityDeath(__instance, info);
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    internal static class Patch_SaveRestore_Save
    {
        [HarmonyPostfix]
        private static void Postfix()
            => GrimmCoreGameHooks.InvokeServerSave();
    }

    [HarmonyPatch(typeof(BasePlayer), "ChatMessage", new[] { typeof(string) })]
    internal static class Patch_BasePlayer_ChatMessage
    {
        [HarmonyPrefix]
        private static bool Prefix(BasePlayer __instance, string msg)
            => GrimmCoreGameHooks.InvokeMessagePlayer(msg, __instance) == null;
    }
}
