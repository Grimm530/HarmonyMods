using System;
using System.Collections.Generic;
using Network;
using ProtoBuf;
using UnityEngine;

namespace GrimmCoreHarmony
{
    /// <summary>
    /// One typed handler list per Oxide-equivalent game hook. Harmony patches call these directly.
    /// Empty snapshots return immediately — Oxide's "nobody subscribed" skip without reflection.
    /// </summary>
    public static class GrimmCoreGameHooks
    {
        internal static readonly GrimmCoreHookList<Action<ItemContainer, Item>> ItemAdded = new GrimmCoreHookList<Action<ItemContainer, Item>>();
        internal static readonly GrimmCoreHookList<Action<ItemContainer, Item>> ItemRemoved = new GrimmCoreHookList<Action<ItemContainer, Item>>();
        internal static readonly GrimmCoreHookList<Func<Item, PlayerInventory, ItemContainerId, int, int, object>> CanMoveItem = new GrimmCoreHookList<Func<Item, PlayerInventory, ItemContainerId, int, int, object>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer>> PlayerDeath1 = new GrimmCoreHookList<Action<BasePlayer>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer, HitInfo>> PlayerDeath2 = new GrimmCoreHookList<Action<BasePlayer, HitInfo>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer, string>> PlayerKicked = new GrimmCoreHookList<Action<BasePlayer, string>>();
        internal static readonly GrimmCoreHookList<Action<LootContainer>> LootSpawn = new GrimmCoreHookList<Action<LootContainer>>();
        internal static readonly GrimmCoreHookList<Func<BasePlayer, BaseEntity, object>> CanLootEntity = new GrimmCoreHookList<Func<BasePlayer, BaseEntity, object>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer, BaseEntity>> LootEntity = new GrimmCoreHookList<Action<BasePlayer, BaseEntity>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer, BaseEntity>> LootEntityEnd = new GrimmCoreHookList<Action<BasePlayer, BaseEntity>>();
        internal static readonly GrimmCoreHookList<Action<PlayerLoot>> PlayerLootEnd = new GrimmCoreHookList<Action<PlayerLoot>>();
        internal static readonly GrimmCoreHookList<Action<SamSite, List<SamSite.ISamSiteTarget>>> SamSiteTargetScan = new GrimmCoreHookList<Action<SamSite, List<SamSite.ISamSiteTarget>>>();
        internal static readonly GrimmCoreHookList<Func<TimedExplosive, BaseEntity, object>> CanExplosiveStick = new GrimmCoreHookList<Func<TimedExplosive, BaseEntity, object>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer>> PlayerConnected = new GrimmCoreHookList<Action<BasePlayer>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer>> PlayerDisconnected1 = new GrimmCoreHookList<Action<BasePlayer>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer, string>> PlayerDisconnected2 = new GrimmCoreHookList<Action<BasePlayer, string>>();
        internal static readonly GrimmCoreHookList<Action<Item, BasePlayer>> ItemHeld = new GrimmCoreHookList<Action<Item, BasePlayer>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer, BaseEntity>> RocketLaunched = new GrimmCoreHookList<Action<BasePlayer, BaseEntity>>();
        internal static readonly GrimmCoreHookList<Func<Planner, Construction, Construction.Target, object>> CanBuild = new GrimmCoreHookList<Func<Planner, Construction, Construction.Target, object>>();
        internal static readonly GrimmCoreHookList<Action<Planner, GameObject>> EntityBuilt = new GrimmCoreHookList<Action<Planner, GameObject>>();
        internal static readonly GrimmCoreHookList<Func<BasePlayer, Planner, Construction, object>> PayForPlacement = new GrimmCoreHookList<Func<BasePlayer, Planner, Construction, object>>();
        internal static readonly GrimmCoreHookList<Func<BasePlayer, BaseEntity, object>> CanPickupEntity = new GrimmCoreHookList<Func<BasePlayer, BaseEntity, object>>();
        internal static readonly GrimmCoreHookList<Action<BaseNetworkable>> EntityKill = new GrimmCoreHookList<Action<BaseNetworkable>>();
        internal static readonly GrimmCoreHookList<Func<BaseNetworkable, object>> EntityKillCancel = new GrimmCoreHookList<Func<BaseNetworkable, object>>();
        internal static readonly GrimmCoreHookList<Func<BasePlayer, HitInfo, object>> HammerHit = new GrimmCoreHookList<Func<BasePlayer, HitInfo, object>>();
        internal static readonly GrimmCoreHookList<Func<Recycler, BasePlayer, object>> RecyclerToggle = new GrimmCoreHookList<Func<Recycler, BasePlayer, object>>();
        internal static readonly GrimmCoreHookList<Action<Deployer>> ItemDeployed = new GrimmCoreHookList<Action<Deployer>>();
        internal static readonly GrimmCoreHookList<Func<BaseProjectile, int, BasePlayer, object>> MagazineReload = new GrimmCoreHookList<Func<BaseProjectile, int, BasePlayer, object>>();
        internal static readonly GrimmCoreHookList<Action<Item, float>> LoseCondition = new GrimmCoreHookList<Action<Item, float>>();
        internal static readonly GrimmCoreHookList<Action<BasePlayer, PlayerTick, bool>> PlayerTick = new GrimmCoreHookList<Action<BasePlayer, PlayerTick, bool>>();
        internal static readonly GrimmCoreHookList<Func<BaseCombatEntity, BasePlayer, object>> StructureRepair = new GrimmCoreHookList<Func<BaseCombatEntity, BasePlayer, object>>();
        internal static readonly GrimmCoreHookList<Func<BaseEntity, object>> EntityGroundMissing = new GrimmCoreHookList<Func<BaseEntity, object>>();
        internal static readonly GrimmCoreHookList<Action<BaseCombatEntity, HitInfo>> EntityDeath = new GrimmCoreHookList<Action<BaseCombatEntity, HitInfo>>();
        internal static readonly GrimmCoreHookList<Action> ServerSave = new GrimmCoreHookList<Action>();
        internal static readonly GrimmCoreHookList<Func<string, BasePlayer, object>> MessagePlayer = new GrimmCoreHookList<Func<string, BasePlayer, object>>();

        public static void RegisterUntyped(string modId, string hook, int priority, Delegate handler)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(hook) || handler == null) return;
            if (!TrySet(modId, hook, priority, handler))
                Debug.LogWarning("[GrimmCore] GameHook register failed for " + modId + " " + hook + " (" + handler.GetType().FullName + ")");
        }

        public static void UnregisterMod(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            ItemAdded.Remove(modId);
            ItemRemoved.Remove(modId);
            CanMoveItem.Remove(modId);
            PlayerDeath1.Remove(modId);
            PlayerDeath2.Remove(modId);
            PlayerKicked.Remove(modId);
            LootSpawn.Remove(modId);
            CanLootEntity.Remove(modId);
            LootEntity.Remove(modId);
            LootEntityEnd.Remove(modId);
            PlayerLootEnd.Remove(modId);
            SamSiteTargetScan.Remove(modId);
            CanExplosiveStick.Remove(modId);
            PlayerConnected.Remove(modId);
            PlayerDisconnected1.Remove(modId);
            PlayerDisconnected2.Remove(modId);
            ItemHeld.Remove(modId);
            RocketLaunched.Remove(modId);
            CanBuild.Remove(modId);
            EntityBuilt.Remove(modId);
            PayForPlacement.Remove(modId);
            CanPickupEntity.Remove(modId);
            EntityKill.Remove(modId);
            EntityKillCancel.Remove(modId);
            HammerHit.Remove(modId);
            RecyclerToggle.Remove(modId);
            ItemDeployed.Remove(modId);
            MagazineReload.Remove(modId);
            LoseCondition.Remove(modId);
            PlayerTick.Remove(modId);
            StructureRepair.Remove(modId);
            EntityGroundMissing.Remove(modId);
            EntityDeath.Remove(modId);
            ServerSave.Remove(modId);
            MessagePlayer.Remove(modId);
        }

        public static void UnregisterHook(string modId, string hook)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(hook)) return;
            switch (hook)
            {
                case "OnItemAddedToContainer": ItemAdded.Remove(modId); break;
                case "OnItemRemovedFromContainer": ItemRemoved.Remove(modId); break;
                case "CanMoveItem": CanMoveItem.Remove(modId); break;
                case "OnPlayerDeath": PlayerDeath1.Remove(modId); PlayerDeath2.Remove(modId); break;
                case "OnPlayerKicked": PlayerKicked.Remove(modId); break;
                case "OnLootSpawn": LootSpawn.Remove(modId); break;
                case "CanLootEntity": CanLootEntity.Remove(modId); break;
                case "OnLootEntity": LootEntity.Remove(modId); break;
                case "OnLootEntityEnd": LootEntityEnd.Remove(modId); break;
                case "OnPlayerLootEnd": PlayerLootEnd.Remove(modId); break;
                case "OnSamSiteTargetScan": SamSiteTargetScan.Remove(modId); break;
                case "CanExplosiveStick": CanExplosiveStick.Remove(modId); break;
                case "OnPlayerConnected": PlayerConnected.Remove(modId); break;
                case "OnPlayerDisconnected": PlayerDisconnected1.Remove(modId); PlayerDisconnected2.Remove(modId); break;
                case "OnItemHeld": ItemHeld.Remove(modId); break;
                case "OnRocketLaunched": RocketLaunched.Remove(modId); break;
                case "CanBuild": CanBuild.Remove(modId); break;
                case "OnEntityBuilt": EntityBuilt.Remove(modId); break;
                case "OnPayForPlacement": PayForPlacement.Remove(modId); break;
                case "CanPickupEntity": CanPickupEntity.Remove(modId); break;
                case "OnEntityKill": EntityKill.Remove(modId); EntityKillCancel.Remove(modId); break;
                case "OnHammerHit": HammerHit.Remove(modId); break;
                case "OnRecyclerToggle": RecyclerToggle.Remove(modId); break;
                case "OnItemDeployed": ItemDeployed.Remove(modId); break;
                case "OnMagazineReload": MagazineReload.Remove(modId); break;
                case "OnLoseCondition": LoseCondition.Remove(modId); break;
                case "OnPlayerTick": PlayerTick.Remove(modId); break;
                case "OnStructureRepair": StructureRepair.Remove(modId); break;
                case "OnEntityGroundMissing": EntityGroundMissing.Remove(modId); break;
                case "OnEntityDeath": EntityDeath.Remove(modId); break;
                case "OnServerSave": ServerSave.Remove(modId); break;
                case "OnMessagePlayer": MessagePlayer.Remove(modId); break;
            }
        }

        public static void ClearAll()
        {
            ItemAdded.Clear();
            ItemRemoved.Clear();
            CanMoveItem.Clear();
            PlayerDeath1.Clear();
            PlayerDeath2.Clear();
            PlayerKicked.Clear();
            LootSpawn.Clear();
            CanLootEntity.Clear();
            LootEntity.Clear();
            LootEntityEnd.Clear();
            PlayerLootEnd.Clear();
            SamSiteTargetScan.Clear();
            CanExplosiveStick.Clear();
            PlayerConnected.Clear();
            PlayerDisconnected1.Clear();
            PlayerDisconnected2.Clear();
            ItemHeld.Clear();
            RocketLaunched.Clear();
            CanBuild.Clear();
            EntityBuilt.Clear();
            PayForPlacement.Clear();
            CanPickupEntity.Clear();
            EntityKill.Clear();
            EntityKillCancel.Clear();
            HammerHit.Clear();
            RecyclerToggle.Clear();
            ItemDeployed.Clear();
            MagazineReload.Clear();
            LoseCondition.Clear();
            PlayerTick.Clear();
            StructureRepair.Clear();
            EntityGroundMissing.Clear();
            EntityDeath.Clear();
            ServerSave.Clear();
            MessagePlayer.Clear();
        }

        public static bool TrySet(string modId, string hook, int priority, Delegate handler)
        {
            switch (hook)
            {
                case "OnItemAddedToContainer": return Set(ItemAdded, modId, priority, handler);
                case "OnItemRemovedFromContainer": return Set(ItemRemoved, modId, priority, handler);
                case "CanMoveItem": return Set(CanMoveItem, modId, priority, handler);
                case "OnPlayerDeath":
                    if (Set(PlayerDeath2, modId, priority, handler)) return true;
                    return Set(PlayerDeath1, modId, priority, handler);
                case "OnPlayerKicked": return Set(PlayerKicked, modId, priority, handler);
                case "OnLootSpawn": return Set(LootSpawn, modId, priority, handler);
                case "CanLootEntity": return Set(CanLootEntity, modId, priority, handler);
                case "OnLootEntity": return Set(LootEntity, modId, priority, handler);
                case "OnLootEntityEnd": return Set(LootEntityEnd, modId, priority, handler);
                case "OnPlayerLootEnd": return Set(PlayerLootEnd, modId, priority, handler);
                case "OnSamSiteTargetScan": return Set(SamSiteTargetScan, modId, priority, handler);
                case "CanExplosiveStick": return Set(CanExplosiveStick, modId, priority, handler);
                case "OnPlayerConnected": return Set(PlayerConnected, modId, priority, handler);
                case "OnPlayerDisconnected":
                    if (Set(PlayerDisconnected2, modId, priority, handler)) return true;
                    return Set(PlayerDisconnected1, modId, priority, handler);
                case "OnItemHeld": return Set(ItemHeld, modId, priority, handler);
                case "OnRocketLaunched": return Set(RocketLaunched, modId, priority, handler);
                case "CanBuild": return Set(CanBuild, modId, priority, handler);
                case "OnEntityBuilt": return Set(EntityBuilt, modId, priority, handler);
                case "OnPayForPlacement": return Set(PayForPlacement, modId, priority, handler);
                case "CanPickupEntity": return Set(CanPickupEntity, modId, priority, handler);
                case "OnEntityKill":
                    if (Set(EntityKillCancel, modId, priority, handler)) return true;
                    return Set(EntityKill, modId, priority, handler);
                case "OnHammerHit": return Set(HammerHit, modId, priority, handler);
                case "OnRecyclerToggle": return Set(RecyclerToggle, modId, priority, handler);
                case "OnItemDeployed": return Set(ItemDeployed, modId, priority, handler);
                case "OnMagazineReload": return Set(MagazineReload, modId, priority, handler);
                case "OnLoseCondition": return Set(LoseCondition, modId, priority, handler);
                case "OnPlayerTick": return Set(PlayerTick, modId, priority, handler);
                case "OnStructureRepair": return Set(StructureRepair, modId, priority, handler);
                case "OnEntityGroundMissing": return Set(EntityGroundMissing, modId, priority, handler);
                case "OnEntityDeath": return Set(EntityDeath, modId, priority, handler);
                case "OnServerSave": return Set(ServerSave, modId, priority, handler);
                case "OnMessagePlayer": return Set(MessagePlayer, modId, priority, handler);
                default: return false;
            }
        }

        private static bool Set<T>(GrimmCoreHookList<T> list, string modId, int priority, Delegate handler) where T : Delegate
        {
            if (TryAdapt(handler, out T typed))
            {
                list.Set(modId, priority, typed);
                return true;
            }
            return false;
        }

        private static bool TryAdapt<T>(Delegate handler, out T typed) where T : Delegate
        {
            typed = null;
            if (handler == null) return false;
            if (handler is T exact)
            {
                typed = exact;
                return true;
            }
            try
            {
                typed = (T)Delegate.CreateDelegate(typeof(T), handler.Target, handler.Method);
                return typed != null;
            }
            catch
            {
                return false;
            }
        }

        public static void InvokeItemAdded(ItemContainer container, Item item)
        {
            var snap = ItemAdded.Snapshot;
            if (snap.Length == 0) return;
            var mods = ItemAdded.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](container, item); }
                catch (Exception ex) { GrimmCoreHookList<Action<ItemContainer, Item>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokeItemRemoved(ItemContainer container, Item item)
        {
            var snap = ItemRemoved.Snapshot;
            if (snap.Length == 0) return;
            var mods = ItemRemoved.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](container, item); }
                catch (Exception ex) { GrimmCoreHookList<Action<ItemContainer, Item>>.Warn(mods, i, ex); }
            }
        }

        public static object InvokeCanMoveItem(Item item, PlayerInventory inv, ItemContainerId target, int slot, int amount)
        {
            var snap = CanMoveItem.Snapshot;
            if (snap.Length == 0) return null;
            var mods = CanMoveItem.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](item, inv, target, slot, amount); }
                catch (Exception ex) { GrimmCoreHookList<Func<Item, PlayerInventory, ItemContainerId, int, int, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static void InvokePlayerDeath(BasePlayer player, HitInfo info)
        {
            var snap1 = PlayerDeath1.Snapshot;
            var mods1 = PlayerDeath1.Mods;
            for (int i = 0; i < snap1.Length; i++)
            {
                try { snap1[i](player); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer>>.Warn(mods1, i, ex); }
            }
            var snap2 = PlayerDeath2.Snapshot;
            if (snap2.Length == 0) return;
            var mods2 = PlayerDeath2.Mods;
            for (int i = 0; i < snap2.Length; i++)
            {
                try { snap2[i](player, info); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer, HitInfo>>.Warn(mods2, i, ex); }
            }
        }

        public static void InvokePlayerKicked(BasePlayer player, string reason)
        {
            var snap = PlayerKicked.Snapshot;
            if (snap.Length == 0) return;
            var mods = PlayerKicked.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](player, reason); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer, string>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokeLootSpawn(LootContainer container)
        {
            var snap = LootSpawn.Snapshot;
            if (snap.Length == 0) return;
            var mods = LootSpawn.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](container); }
                catch (Exception ex) { GrimmCoreHookList<Action<LootContainer>>.Warn(mods, i, ex); }
            }
        }

        public static object InvokeCanLootEntity(BasePlayer player, BaseEntity entity)
        {
            var snap = CanLootEntity.Snapshot;
            if (snap.Length == 0) return null;
            var mods = CanLootEntity.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](player, entity); }
                catch (Exception ex) { GrimmCoreHookList<Func<BasePlayer, BaseEntity, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static void InvokeLootEntity(BasePlayer player, BaseEntity entity)
        {
            var snap = LootEntity.Snapshot;
            if (snap.Length == 0) return;
            var mods = LootEntity.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](player, entity); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer, BaseEntity>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokeLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            var snap = LootEntityEnd.Snapshot;
            if (snap.Length == 0) return;
            var mods = LootEntityEnd.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](player, entity); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer, BaseEntity>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokePlayerLootEnd(PlayerLoot loot)
        {
            var snap = PlayerLootEnd.Snapshot;
            if (snap.Length == 0) return;
            var mods = PlayerLootEnd.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](loot); }
                catch (Exception ex) { GrimmCoreHookList<Action<PlayerLoot>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokeSamSiteTargetScan(SamSite sam, List<SamSite.ISamSiteTarget> list)
        {
            var snap = SamSiteTargetScan.Snapshot;
            if (snap.Length == 0) return;
            var mods = SamSiteTargetScan.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](sam, list); }
                catch (Exception ex) { GrimmCoreHookList<Action<SamSite, List<SamSite.ISamSiteTarget>>>.Warn(mods, i, ex); }
            }
        }

        public static object InvokeCanExplosiveStick(TimedExplosive exp, BaseEntity entity)
        {
            var snap = CanExplosiveStick.Snapshot;
            if (snap.Length == 0) return null;
            var mods = CanExplosiveStick.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](exp, entity); }
                catch (Exception ex) { GrimmCoreHookList<Func<TimedExplosive, BaseEntity, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static void InvokePlayerConnected(BasePlayer player)
        {
            var snap = PlayerConnected.Snapshot;
            if (snap.Length == 0) return;
            var mods = PlayerConnected.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](player); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokePlayerDisconnected(BasePlayer player, string reason)
        {
            var snap1 = PlayerDisconnected1.Snapshot;
            var mods1 = PlayerDisconnected1.Mods;
            for (int i = 0; i < snap1.Length; i++)
            {
                try { snap1[i](player); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer>>.Warn(mods1, i, ex); }
            }
            var snap2 = PlayerDisconnected2.Snapshot;
            if (snap2.Length == 0) return;
            var mods2 = PlayerDisconnected2.Mods;
            for (int i = 0; i < snap2.Length; i++)
            {
                try { snap2[i](player, reason); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer, string>>.Warn(mods2, i, ex); }
            }
        }

        public static void InvokeItemHeld(Item item, BasePlayer player)
        {
            var snap = ItemHeld.Snapshot;
            if (snap.Length == 0) return;
            var mods = ItemHeld.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](item, player); }
                catch (Exception ex) { GrimmCoreHookList<Action<Item, BasePlayer>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokeRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            var snap = RocketLaunched.Snapshot;
            if (snap.Length == 0) return;
            var mods = RocketLaunched.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](player, entity); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer, BaseEntity>>.Warn(mods, i, ex); }
            }
        }

        public static object InvokeCanBuild(Planner plan, Construction prefab, Construction.Target target)
        {
            var snap = CanBuild.Snapshot;
            if (snap.Length == 0) return null;
            var mods = CanBuild.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](plan, prefab, target); }
                catch (Exception ex) { GrimmCoreHookList<Func<Planner, Construction, Construction.Target, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static void InvokeEntityBuilt(Planner planner, GameObject go)
        {
            var snap = EntityBuilt.Snapshot;
            if (snap.Length == 0) return;
            var mods = EntityBuilt.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](planner, go); }
                catch (Exception ex) { GrimmCoreHookList<Action<Planner, GameObject>>.Warn(mods, i, ex); }
            }
        }

        public static object InvokePayForPlacement(BasePlayer player, Planner planner, Construction component)
        {
            var snap = PayForPlacement.Snapshot;
            if (snap.Length == 0) return null;
            var mods = PayForPlacement.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](player, planner, component); }
                catch (Exception ex) { GrimmCoreHookList<Func<BasePlayer, Planner, Construction, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static object InvokeCanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            var snap = CanPickupEntity.Snapshot;
            if (snap.Length == 0) return null;
            var mods = CanPickupEntity.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](player, entity); }
                catch (Exception ex) { GrimmCoreHookList<Func<BasePlayer, BaseEntity, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        /// <summary>Observers always run. First non-null cancel result skips Kill (Oxide ReturnBehavior 1).</summary>
        public static bool InvokeEntityKill(BaseNetworkable entity)
        {
            if (entity == null) return true;
            var observe = EntityKill.Snapshot;
            var obsMods = EntityKill.Mods;
            for (int i = 0; i < observe.Length; i++)
            {
                try { observe[i](entity); }
                catch (Exception ex) { GrimmCoreHookList<Action<BaseNetworkable>>.Warn(obsMods, i, ex); }
            }
            var cancel = EntityKillCancel.Snapshot;
            if (cancel.Length == 0) return true;
            var mods = EntityKillCancel.Mods;
            for (int i = 0; i < cancel.Length; i++)
            {
                object v;
                try { v = cancel[i](entity); }
                catch (Exception ex) { GrimmCoreHookList<Func<BaseNetworkable, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return false;
            }
            return true;
        }

        public static object InvokeHammerHit(BasePlayer player, HitInfo info)
        {
            var snap = HammerHit.Snapshot;
            if (snap.Length == 0) return null;
            var mods = HammerHit.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](player, info); }
                catch (Exception ex) { GrimmCoreHookList<Func<BasePlayer, HitInfo, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static object InvokeRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            var snap = RecyclerToggle.Snapshot;
            if (snap.Length == 0) return null;
            var mods = RecyclerToggle.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](recycler, player); }
                catch (Exception ex) { GrimmCoreHookList<Func<Recycler, BasePlayer, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static void InvokeItemDeployed(Deployer deployer)
        {
            var snap = ItemDeployed.Snapshot;
            if (snap.Length == 0) return;
            var mods = ItemDeployed.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](deployer); }
                catch (Exception ex) { GrimmCoreHookList<Action<Deployer>>.Warn(mods, i, ex); }
            }
        }

        public static object InvokeMagazineReload(BaseProjectile projectile, int amount, BasePlayer player)
        {
            var snap = MagazineReload.Snapshot;
            if (snap.Length == 0) return null;
            var mods = MagazineReload.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](projectile, amount, player); }
                catch (Exception ex) { GrimmCoreHookList<Func<BaseProjectile, int, BasePlayer, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static void InvokeLoseCondition(Item item, float amount)
        {
            var snap = LoseCondition.Snapshot;
            if (snap.Length == 0) return;
            var mods = LoseCondition.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](item, amount); }
                catch (Exception ex) { GrimmCoreHookList<Action<Item, float>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokePlayerTick(BasePlayer player, PlayerTick msg, bool stalled)
        {
            var snap = PlayerTick.Snapshot;
            if (snap.Length == 0) return;
            var mods = PlayerTick.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](player, msg, stalled); }
                catch (Exception ex) { GrimmCoreHookList<Action<BasePlayer, PlayerTick, bool>>.Warn(mods, i, ex); }
            }
        }

        public static object InvokeStructureRepair(BaseCombatEntity entity, BasePlayer player)
        {
            var snap = StructureRepair.Snapshot;
            if (snap.Length == 0) return null;
            var mods = StructureRepair.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](entity, player); }
                catch (Exception ex) { GrimmCoreHookList<Func<BaseCombatEntity, BasePlayer, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static object InvokeEntityGroundMissing(BaseEntity entity)
        {
            var snap = EntityGroundMissing.Snapshot;
            if (snap.Length == 0) return null;
            var mods = EntityGroundMissing.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](entity); }
                catch (Exception ex) { GrimmCoreHookList<Func<BaseEntity, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }

        public static void InvokeEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var snap = EntityDeath.Snapshot;
            if (snap.Length == 0) return;
            var mods = EntityDeath.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](entity, info); }
                catch (Exception ex) { GrimmCoreHookList<Action<BaseCombatEntity, HitInfo>>.Warn(mods, i, ex); }
            }
        }

        public static void InvokeServerSave()
        {
            var snap = ServerSave.Snapshot;
            if (snap.Length == 0) return;
            var mods = ServerSave.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                try { snap[i](); }
                catch (Exception ex) { GrimmCoreHookList<Action>.Warn(mods, i, ex); }
            }
        }

        public static object InvokeMessagePlayer(string message, BasePlayer player)
        {
            var snap = MessagePlayer.Snapshot;
            if (snap.Length == 0) return null;
            var mods = MessagePlayer.Mods;
            for (int i = 0; i < snap.Length; i++)
            {
                object v;
                try { v = snap[i](message, player); }
                catch (Exception ex) { GrimmCoreHookList<Func<string, BasePlayer, object>>.Warn(mods, i, ex); continue; }
                if (v != null) return v;
            }
            return null;
        }
    }
}
