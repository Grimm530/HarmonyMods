using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using P = Oxide.Plugins.VirtualItems;

namespace VirtualItemsHarmony.Patches
{
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Insert), new[] { typeof(Item) })]
    public static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result) return;
            try { P.Dispatch_OnItemAddedToContainer(__instance, item); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnItemAddedToContainer: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.RemoveFromContainer))]
    public static class Item_RemoveFromContainer_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item __instance, out ItemContainer __state) => __state = __instance?.parent;
        [HarmonyPostfix]
        public static void Postfix(Item __instance, ItemContainer __state)
        {
            if (__state == null) return;
            try { P.Dispatch_OnItemRemovedFromContainer(__state, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnItemRemovedFromContainer: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.MoveToContainer))]
    public static class Item_MoveToContainer_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Item __instance, ItemContainer newcontainer, int iTargetPos, BasePlayer sourcePlayer)
        {
            if (sourcePlayer?.inventory == null) return true;
            try
            {
                var blocked = P.Dispatch_CanMoveItem(__instance, sourcePlayer.inventory, newcontainer?.uid ?? default, iTargetPos, __instance?.amount ?? 0);
                if (blocked != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] CanMoveItem: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Die), new[] { typeof(HitInfo) })]
    public static class BasePlayer_Die_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, HitInfo info)
        {
            try { P.Dispatch_OnPlayerDeath(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnPlayerDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.Kick), new[] { typeof(string), typeof(bool) })]
    public static class BasePlayer_Kick_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BasePlayer __instance, string reason)
        {
            try { P.Dispatch_OnPlayerKicked(__instance, reason); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnPlayerKicked: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.SpawnLoot))]
    public static class LootContainer_SpawnLoot_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LootContainer __instance)
        {
            try { P.Dispatch_OnLootSpawn(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnLootSpawn: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.StartLootingEntity), new[] { typeof(BaseEntity), typeof(bool) })]
    public static class PlayerLoot_StartLootingEntity_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerLoot __instance, BaseEntity targetEntity, ref bool __result)
        {
            var player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return true;
            try
            {
                if (P.Dispatch_CanLootEntity(player, targetEntity) != null)
                { __result = false; return false; }
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] CanLootEntity: " + ex.Message); }
            return true;
        }
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity)
        {
            var player = __instance?.baseEntity;
            if (player == null || targetEntity == null) return;
            try { P.Dispatch_OnLootEntity(player, targetEntity); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnLootEntity: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerLoot __instance, out BaseEntity __state)
        {
            __state = __instance?.entitySource;
            try { P.Dispatch_OnPlayerLootEnd(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnPlayerLootEnd: " + ex.Message); }
        }
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity __state)
        {
            var player = __instance?.baseEntity;
            if (player == null || __state == null) return;
            try { P.Dispatch_OnLootEntityEnd(player, __state); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnLootEntityEnd: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SamSite), "AddTargetSet")]
    public static class SamSite_AddTargetSet_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(SamSite __instance, List<SamSite.ISamSiteTarget> allTargets)
        {
            try { P.Dispatch_OnSamSiteTargetScan(__instance, allTargets); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnSamSiteTargetScan: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Spawn))]
    public static class BaseNetworkable_Spawn_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseNetworkable __instance)
        {
            try { P.Dispatch_OnEntitySpawned(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnEntitySpawned: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(TimedExplosive), nameof(TimedExplosive.CanStickTo))]
    public static class TimedExplosive_CanStickTo_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(TimedExplosive __instance, BaseEntity entity, ref bool __result)
        {
            try
            {
                var r = P.Dispatch_CanExplosiveStick(__instance, entity);
                if (r is bool b) { __result = b; return false; }
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] CanExplosiveStick: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public static class BasePlayer_PlayerInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { P.Dispatch_OnPlayerConnected(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnPlayerConnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { P.Dispatch_OnPlayerDisconnected(__instance, null); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "UpdateActiveItem", new[] { typeof(ItemId) })]
    public static class BasePlayer_UpdateActiveItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { P.Dispatch_OnItemHeld(__instance?.GetActiveItem(), __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnItemHeld: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseLauncher), nameof(BaseLauncher.ProjectileLaunched_Server))]
    public static class BaseLauncher_ProjectileLaunched_Server_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseLauncher __instance, ServerProjectile justLaunched)
        {
            var player = __instance?.GetOwnerPlayer();
            var entity = justLaunched?.baseEntity as BaseEntity;
            if (player == null || entity == null) return;
            try { P.Dispatch_OnRocketLaunched(player, entity); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnRocketLaunched: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Planner), "DoBuild", new[] { typeof(Construction.Target), typeof(Construction) })]
    public static class Planner_DoBuild_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Planner __instance, Construction.Target target, Construction component)
        {
            try
            {
                if (P.Dispatch_CanBuild(__instance, component, target) != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] CanBuild: " + ex.Message); }
            return true;
        }
        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result)
        {
            if (__result == null) return;
            try { P.Dispatch_OnEntityBuilt(__instance, __result.gameObject); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnEntityBuilt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Planner), nameof(Planner.PayForPlacement))]
    public static class Planner_PayForPlacement_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Planner __instance, BasePlayer player, Construction component)
        {
            try
            {
                if (P.Dispatch_OnPayForPlacement(player, __instance, component) != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnPayForPlacement: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), "CanCompletePickup")]
    public static class BaseCombatEntity_CanCompletePickup_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, BasePlayer player, ref bool __result)
        {
            try
            {
                var r = P.Dispatch_CanPickupEntity(player, __instance);
                if (r != null) { __result = false; return false; }
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] CanPickupEntity: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            try { P.Dispatch_OnEntityKill(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnEntityKill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseMelee), nameof(BaseMelee.DoAttackShared))]
    public static class BaseMelee_DoAttackShared_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseMelee __instance, HitInfo info)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null) return true;
            try
            {
                if (P.Dispatch_OnHammerHit(player, info) != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnHammerHit: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Recycler), "SVSwitch")]
    public static class Recycler_SVSwitch_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Recycler __instance, BaseEntity.RPCMessage msg)
        {
            try { P.Dispatch_OnRecyclerToggle(__instance, msg.player); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnRecyclerToggle: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Recycler), "RecycleThink")]
    public static class Recycler_RecycleThink_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Recycler __instance)
        {
            // OnItemRecycle is invoked from inside RecycleThink per-slot; postfix-safe observation only.
        }
    }

    [HarmonyPatch(typeof(Deployer), "DoDeploy_Regular")]
    public static class Deployer_DoDeploy_Regular_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Deployer __instance)
        {
            try { P.Dispatch_OnItemDeployed(__instance); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnItemDeployed: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseProjectile), nameof(BaseProjectile.TryReloadMagazine))]
    public static class BaseProjectile_TryReloadMagazine_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseProjectile __instance, ref bool __result)
        {
            try
            {
                var r = P.Dispatch_OnMagazineReload(__instance, 0, __instance.GetOwnerPlayer());
                if (r is bool b) { __result = b; return false; }
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnMagazineReload: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.LoseCondition))]
    public static class Item_LoseCondition_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Item __instance, float amount)
        {
            try { P.Dispatch_OnLoseCondition(__instance, amount); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnLoseCondition: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "OnReceiveTick", new[] { typeof(PlayerTick), typeof(bool) })]
    public static class BasePlayer_OnReceiveTick_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance, PlayerTick msg, bool wasPlayerStalled)
        {
            try { P.Dispatch_OnPlayerTick(__instance, msg, wasPlayerStalled); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnPlayerTick: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.DoRepair))]
    public static class BaseCombatEntity_DoRepair_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, BasePlayer player)
        {
            try
            {
                if (P.Dispatch_OnStructureRepair(__instance, player) != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnStructureRepair: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(DestroyOnGroundMissing), "OnGroundMissing")]
    public static class DestroyOnGroundMissing_OnGroundMissing_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(DestroyOnGroundMissing __instance)
        {
            try
            {
                var e = GameObjectEx.ToBaseEntity(__instance.gameObject);
                if (e != null && P.Dispatch_OnEntityGroundMissing(e) != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnEntityGroundMissing: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), nameof(BaseCombatEntity.Die), new[] { typeof(HitInfo) })]
    public static class BaseCombatEntity_Die_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseCombatEntity __instance, HitInfo info)
        {
            try { P.Dispatch_OnEntityDeath(__instance, info); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnEntityDeath: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveRestore), nameof(SaveRestore.Save), typeof(string), typeof(bool))]
    public static class SaveRestore_Save_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { P.Dispatch_OnServerSave(); }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnServerSave: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BasePlayer), "ChatMessage", new[] { typeof(string) })]
    public static class BasePlayer_ChatMessage_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, string msg)
        {
            try
            {
                if (P.Dispatch_OnMessagePlayer(msg, __instance) != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[VirtualItems] OnMessagePlayer: " + ex.Message); }
            return true;
        }
    }
}
