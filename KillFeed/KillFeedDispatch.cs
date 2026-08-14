using System;
using UnityEngine;

namespace Oxide.Plugins
{
    public partial class KillFeed
    {
        internal static void SetInstance(KillFeed inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;
        internal static KillFeed GetModInstance() => Instance;

        public void CallInit()
        {
            try
            {
                var mi = GetType().GetMethod("Init", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                mi?.Invoke(this, null);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[KillFeed] Init failed: " + ex.Message); }
            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[KillFeed] LoadDefaultMessages failed: " + ex.Message); }
        }

        public void CallLoaded()
        {
            try
            {
                var mi = GetType().GetMethod("Loaded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                mi?.Invoke(this, null);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[KillFeed] Loaded failed: " + ex.Message); }
        }

        public void CallOnServerInitialized()
        {
            try
            {
                var mi = GetType().GetMethod("OnServerInitialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                      ?? GetType().GetMethod("OnServerInitialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                mi?.Invoke(this, mi.GetParameters().Length == 0 ? null : new object[mi.GetParameters().Length]);
            }
            catch (Exception ex) { UnityEngine.Debug.LogError("[KillFeed] OnServerInitialized failed: " + ex); }
        }

        public void CallUnload()
        {
            try
            {
                var mi = GetType().GetMethod("Unload", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                mi?.Invoke(this, null);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[KillFeed] Unload failed: " + ex.Message); }
        }

        public static bool IsHookSubscribed(string hookName)
        {
            var inst = GetModInstance();
            return inst == null || inst.IsSubscribed(hookName);
        }

        static bool ArgsCompatible(System.Reflection.ParameterInfo[] ps, object[] args)
        {
            if (ps.Length != args.Length) return false;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i].ParameterType;
                var a = args[i];
                if (a == null)
                {
                    if (p.IsValueType && Nullable.GetUnderlyingType(p) == null) return false;
                    continue;
                }
                if (!p.IsInstanceOfType(a)) return false;
            }
            return true;
        }

        static int MatchScore(System.Reflection.ParameterInfo[] ps, object[] args)
        {
            int score = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                if (args[i] == null) continue;
                if (ps[i].ParameterType == args[i].GetType()) score += 1000;
                else score += 1;
            }
            return score;
        }

        static System.Reflection.MethodInfo FindHook(KillFeed inst, string hook, object[] args)
        {
            const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.MethodInfo best = null;
            int bestScore = -1;
            foreach (var mi in inst.GetType().GetMethods(bf))
            {
                if (mi.Name != hook) continue;
                var ps = mi.GetParameters();
                if (!ArgsCompatible(ps, args)) continue;
                int score = MatchScore(ps, args);
                if (score > bestScore)
                {
                    best = mi;
                    bestScore = score;
                }
            }
            return best;
        }

        static T CallHook<T>(string hook, params object[] args)
        {
            var inst = GetModInstance();
            if (inst == null || !inst.IsSubscribed(hook)) return default;
            try
            {
                var mi = FindHook(inst, hook, args);
                if (mi == null) return default;
                var r = mi.Invoke(inst, args);
                if (r is T t) return t;
                return default;
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[KillFeed] " + hook + ": " + (ex.InnerException ?? ex).Message); }
            return default;
        }

        static void CallHookVoid(string hook, params object[] args)
        {
            var inst = GetModInstance();
            if (inst == null || !inst.IsSubscribed(hook)) return;
            try
            {
                FindHook(inst, hook, args)?.Invoke(inst, args);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[KillFeed] " + hook + ": " + (ex.InnerException ?? ex).Message); }
        }

        static object CallHookObj(string hook, params object[] args)
        {
            var inst = GetModInstance();
            if (inst == null || !inst.IsSubscribed(hook)) return null;
            try
            {
                return FindHook(inst, hook, args)?.Invoke(inst, args);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[KillFeed] " + hook + ": " + (ex.InnerException ?? ex).Message); return null; }
        }

        public static void Dispatch_OnItemAddedToContainer(ItemContainer container, Item item)
            => CallHookVoid("OnItemAddedToContainer", container, item);
        public static void Dispatch_OnItemRemovedFromContainer(ItemContainer container, Item item)
            => CallHookVoid("OnItemRemovedFromContainer", container, item);
        public static object Dispatch_CanMoveItem(Item item, PlayerInventory inv, ItemContainerId target, int slot, int amount)
            => CallHookObj("CanMoveItem", item, inv, target, slot, amount);
        public static void Dispatch_OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            CallHookVoid("OnPlayerDeath", player);
            CallHookVoid("OnPlayerDeath", player, info);
        }
        public static void Dispatch_OnPlayerKicked(BasePlayer player, string reason)
            => CallHookVoid("OnPlayerKicked", player, reason);
        public static void Dispatch_OnLootSpawn(LootContainer container)
            => CallHookVoid("OnLootSpawn", container);
        public static object Dispatch_CanLootEntity(BasePlayer player, BaseEntity entity)
            => CallHookObj("CanLootEntity", player, entity);
        public static void Dispatch_OnSamSiteTargetScan(SamSite sam, System.Collections.Generic.List<SamSite.ISamSiteTarget> list)
            => CallHookVoid("OnSamSiteTargetScan", sam, list);
        public static object Dispatch_OnPlayerCommand(BasePlayer player, string command, string[] args)
            => CallHookObj("OnPlayerCommand", player, command, args);
        public static void Dispatch_OnEntitySpawned(BaseNetworkable entity)
            => CallHookVoid("OnEntitySpawned", entity);
        public static object Dispatch_CanExplosiveStick(TimedExplosive exp, BaseEntity entity)
            => CallHookObj("CanExplosiveStick", exp, entity);
        public static void Dispatch_OnPlayerConnected(BasePlayer player)
            => CallHookVoid("OnPlayerConnected", player);
        public static void Dispatch_OnPlayerDisconnected(BasePlayer player, string reason = null)
        {
            CallHookVoid("OnPlayerDisconnected", player);
            if (reason != null) CallHookVoid("OnPlayerDisconnected", player, reason);
        }
        public static void Dispatch_OnItemHeld(Item item, BasePlayer player)
            => CallHookVoid("OnItemHeld", item, player);
        public static void Dispatch_OnRocketLaunched(BasePlayer player, BaseEntity entity)
            => CallHookVoid("OnRocketLaunched", player, entity);
        public static void Dispatch_OnEntityBuilt(Planner planner, GameObject go)
            => CallHookVoid("OnEntityBuilt", planner, go);
        public static object Dispatch_CanPickupEntity(BasePlayer player, BaseEntity entity)
            => CallHookObj("CanPickupEntity", player, entity);
        public static object Dispatch_OnEntityKill(BaseNetworkable entity)
            => CallHookObj("OnEntityKill", entity);
        public static void Dispatch_OnLootEntity(BasePlayer player, BaseEntity entity)
            => CallHookVoid("OnLootEntity", player, entity);
        public static void Dispatch_OnLootEntityEnd(BasePlayer player, BaseEntity entity)
            => CallHookVoid("OnLootEntityEnd", player, entity);
        public static void Dispatch_OnPlayerLootEnd(PlayerLoot loot)
            => CallHookVoid("OnPlayerLootEnd", loot);
        public static object Dispatch_OnHammerHit(BasePlayer player, HitInfo info)
            => CallHookObj("OnHammerHit", player, info);
        public static object Dispatch_OnRecyclerToggle(Recycler recycler, BasePlayer player)
            => CallHookObj("OnRecyclerToggle", recycler, player);
        public static object Dispatch_OnItemRecycle(Item item, Recycler recycler)
            => CallHookObj("OnItemRecycle", item, recycler);
        public static object Dispatch_OnPayForPlacement(BasePlayer player, Planner planner, Construction component)
            => CallHookObj("OnPayForPlacement", player, planner);
        public static object Dispatch_CanBuild(Planner plan, Construction prefab, Construction.Target target)
            => CallHookObj("CanBuild", plan, prefab, target);
        public static void Dispatch_OnItemDeployed(Deployer d)
            => CallHookVoid("OnItemDeployed", d);
        public static object Dispatch_OnMagazineReload(BaseProjectile bp, int amount, BasePlayer p)
            => CallHookObj("OnMagazineReload", bp, amount, p);
        public static void Dispatch_OnLoseCondition(Item item, float amount)
            => CallHookVoid("OnLoseCondition", item, amount);
        public static void Dispatch_OnPlayerTick(BasePlayer p, PlayerTick msg, bool stalled)
            => CallHookVoid("OnPlayerTick", p, msg, stalled);
        public static object Dispatch_OnStructureRepair(BaseCombatEntity e, BasePlayer p)
            => CallHookObj("OnStructureRepair", e, p);
        public static object Dispatch_OnEntityGroundMissing(BaseEntity e)
            => CallHookObj("OnEntityGroundMissing", e);
        public static object Dispatch_OnServerCommand(ConsoleSystem.Arg arg)
            => CallHookObj("OnServerCommand", arg);
        public static object Dispatch_OnMessagePlayer(string message, BasePlayer player)
            => CallHookObj("OnMessagePlayer", message, player);
        public static object Dispatch_CanUseWires(BasePlayer player)
            => CallHookObj("CanUseWires", player);
        public static void Dispatch_OnEntityDeath(BaseCombatEntity entity, HitInfo info)
            => CallHookVoid("OnEntityDeath", entity, info);
        public static void Dispatch_OnServerSave()
            => CallHookVoid("OnServerSave");
        public static object Dispatch_CanAffordToPlace(BasePlayer player, Planner planner, Construction component)
            => CallHookObj("CanAffordToPlace", player, planner, component);
    }
}
