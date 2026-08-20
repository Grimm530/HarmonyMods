using System.Linq;
using System.Reflection;
using Facepunch.Rust;
using HarmonyLib;
using UnityEngine;
using P = Oxide.Plugins.IndustrialRecycler;

namespace IndustrialRecyclerHarmony.Patches
{
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.Insert), new[] { typeof(Item) })]
    public static class ItemContainer_Insert_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemContainer __instance, Item item, bool __result)
        {
            if (!__result) return;
            try { P.Dispatch_OnItemAddedToContainer(__instance, item); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnItemAddedToContainer: " + ex.Message); }
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
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnItemRemovedFromContainer: " + ex.Message); }
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
            if (!(targetEntity is Recycler) && !(targetEntity is StorageContainer)) return true;
            try
            {
                if (P.Dispatch_CanLootEntity(player, targetEntity) != null)
                { __result = false; return false; }
            }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] CanLootEntity: " + ex.Message); }
            return true;
        }
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity targetEntity)
        {
            var player = __instance?.baseEntity;
            if (player == null || !(targetEntity is Recycler recycler)) return;
            try { P.Dispatch_OnLootEntity(player, recycler); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnLootEntity: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class PlayerLoot_Clear_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayerLoot __instance, out BaseEntity __state)
        {
            __state = __instance?.entitySource;
            var player = __instance?.baseEntity;
            if (player == null || player.IsDestroyed) return;
            try { P.Dispatch_OnPlayerLootEnd(__instance); }
            catch (System.Exception) { }
        }
        [HarmonyPostfix]
        public static void Postfix(PlayerLoot __instance, BaseEntity __state)
        {
            var player = __instance?.baseEntity;
            if (player == null || !(__state is Recycler recycler)) return;
            try { P.Dispatch_OnLootEntityEnd(player, recycler); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnLootEntityEnd: " + ex.Message); }
        }
    }

    [HarmonyPatch]
    public static class Planner_DoBuild_Patch
    {
        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(Planner))
                .FirstOrDefault(m => m.Name == "DoBuild" && m.ReturnType == typeof(BaseEntity));

        [HarmonyPrefix]
        public static void Prefix(Planner __instance, out ulong __state)
        {
            __state = 0;
            var item = __instance?.GetOwnerItem();
            if (item != null)
                __state = item.skin;
        }

        [HarmonyPostfix]
        public static void Postfix(Planner __instance, BaseEntity __result, ulong __state)
        {
            if (__result == null) return;
            if (__result.skinID == 0 && __state != 0)
                __result.skinID = __state;
            var plugin = P.GetModInstance();
            if (plugin == null) return;
            try { plugin.HandlePlacedRecyclerBox(__instance.GetOwnerPlayer(), __result); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnEntityBuilt: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Analytics.Azure), nameof(Analytics.Azure.OnEntityBuilt))]
    public static class Analytics_OnEntityBuilt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseEntity entity, BasePlayer player)
        {
            var plugin = P.GetModInstance();
            if (plugin == null || entity == null || player == null) return;
            try { plugin.HandlePlacedRecyclerBox(player, entity); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnEntityBuilt(Analytics): " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseCombatEntity), "CanCompletePickup")]
    public static class BaseCombatEntity_CanCompletePickup_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseCombatEntity __instance, BasePlayer player, ref bool __result)
        {
            if (!(__instance is IndustrialStorageAdaptor)) return true;
            try
            {
                var r = P.Dispatch_CanPickupEntity(player, __instance);
                if (r != null) { __result = false; return false; }
            }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] CanPickupEntity: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BaseNetworkable), nameof(BaseNetworkable.Kill), typeof(BaseNetworkable.DestroyMode), typeof(bool))]
    public static class BaseNetworkable_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(BaseNetworkable __instance)
        {
            if (!(__instance is Recycler recycler)) return;
            try { P.Dispatch_OnEntityKill(recycler); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnEntityKill: " + ex.Message); }
        }
    }

    // Hammer.DoAttackShared overrides BaseMelee and does not call base — patch Hammer or pickup never runs.
    [HarmonyPatch(typeof(Hammer), nameof(Hammer.DoAttackShared))]
    public static class Hammer_DoAttackShared_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Hammer __instance, HitInfo info)
        {
            var player = __instance?.GetOwnerPlayer();
            if (player == null || info == null) return true;
            try
            {
                if (P.Dispatch_OnHammerHit(player, info) != null) return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnHammerHit: " + ex.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected))]
    public static class BasePlayer_OnDisconnected_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BasePlayer __instance)
        {
            try { P.Dispatch_OnPlayerDisconnected(__instance, null); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnPlayerDisconnected: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Recycler), "SVSwitch")]
    public static class Recycler_SVSwitch_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Recycler __instance, BaseEntity.RPCMessage msg)
        {
            try { P.Dispatch_OnRecyclerToggle(__instance, msg.player); }
            catch (System.Exception ex) { Debug.LogWarning("[IndustrialRecycler] OnRecyclerToggle: " + ex.Message); }
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
}
