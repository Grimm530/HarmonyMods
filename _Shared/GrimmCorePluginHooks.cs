using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Binds a ported plugin's Oxide-style hook methods onto 0GrimmCore's typed game-hook bus.
/// Linked by feature mods; no reference to 0GrimmCore.dll.
/// </summary>
public static class GrimmCorePluginHooks
{
    private const BindingFlags MethodFlags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly HashSet<string> Catalog = new HashSet<string>(StringComparer.Ordinal)
    {
        "OnEntitySpawned", "OnItemAddedToContainer", "OnItemRemovedFromContainer", "CanMoveItem", "OnPlayerDeath", "OnPlayerKicked", "OnLootSpawn", "CanLootEntity", "OnLootEntity", "OnLootEntityEnd",
        "OnPlayerLootEnd", "OnSamSiteTargetScan", "CanExplosiveStick", "OnPlayerConnected", "OnPlayerDisconnected", "OnItemHeld", "OnRocketLaunched", "CanBuild", "OnEntityBuilt", "OnPayForPlacement",
        "CanPickupEntity", "OnEntityKill", "OnHammerHit", "OnRecyclerToggle", "OnItemDeployed", "OnMagazineReload", "OnLoseCondition", "OnPlayerTick", "OnStructureRepair", "OnEntityGroundMissing",
        "OnEntityDeath", "OnServerSave", "OnMessagePlayer"
    };

    public static int Bind(string modId, object plugin, int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || plugin == null)
            return 0;

        Unbind(modId);
        var found = new Dictionary<string, List<MethodInfo>>(32);
        var type = plugin.GetType();
        while (type != null && type != typeof(object))
        {
            foreach (var method in type.GetMethods(MethodFlags))
            {
                if (method.IsSpecialName || method.ContainsGenericParameters || !Catalog.Contains(method.Name))
                    continue;
                if (!found.TryGetValue(method.Name, out var list))
                {
                    list = new List<MethodInfo>(2);
                    found[method.Name] = list;
                }
                list.Add(method);
            }
            type = type.BaseType;
        }

        int bound = 0;
        foreach (var pair in found)
        {
            if (!IsHookEnabled(plugin, pair.Key))
                continue;

            var methods = pair.Value.ToArray();
            SortMostDerivedFirst(methods);
            if (pair.Key == "OnEntitySpawned")
            {
                var spawnMethods = methods;
                var spawnPlugin = plugin;
                if (GrimmCoreBridge.RegisterSpawnPostfix(modId, priority, e => InvokeBest(spawnPlugin, spawnMethods, e)))
                    bound++;
            }
            else
            {
                var handler = Adapt(plugin, pair.Key, methods);
                if (handler != null && GrimmCoreBridge.RegisterGameHook(modId, pair.Key, priority, handler))
                    bound++;
            }
        }

        if (bound > 0)
            Debug.Log("[GrimmCore] " + modId + " bound " + bound + " typed game hook(s)");
        return bound;
    }

    public static void Unbind(string modId)
    {
        GrimmCoreBridge.UnregisterGameHookMod(modId);
        GrimmCoreBridge.UnregisterSpawnMod(modId);
    }

    private static bool IsHookEnabled(object plugin, string hook)
    {
        try
        {
            var method = plugin.GetType().GetMethod("IsSubscribed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (method == null)
                return true;
            var result = method.Invoke(plugin, new object[] { hook });
            return !(result is bool b) || b;
        }
        catch
        {
            return true;
        }
    }

    private static void SortMostDerivedFirst(MethodInfo[] methods)
    {
        Array.Sort(methods, (a, b) =>
        {
            var pa = a.GetParameters();
            var pb = b.GetParameters();
            if (pa.Length == 0 || pb.Length == 0)
                return pb.Length.CompareTo(pa.Length);
            var ta = pa[0].ParameterType;
            var tb = pb[0].ParameterType;
            if (ta == tb)
                return 0;
            if (ta.IsAssignableFrom(tb))
                return 1;
            return tb.IsAssignableFrom(ta) ? -1 : 0;
        });
    }

    private static Delegate Adapt(object plugin, string hook, MethodInfo[] methods)
    {
        switch (hook)
        {
            case "OnItemAddedToContainer":
            case "OnItemRemovedFromContainer":
                return AdaptAction<ItemContainer, Item>(plugin, methods);
            case "CanMoveItem":
                return AdaptFunc<Item, PlayerInventory, ItemContainerId, int, int>(plugin, methods);
            case "OnPlayerDeath":
                return HasArity(methods, 2)
                    ? AdaptAction<BasePlayer, HitInfo>(plugin, methods)
                    : AdaptAction<BasePlayer>(plugin, methods);
            case "OnPlayerKicked":
                return AdaptAction<BasePlayer, string>(plugin, methods);
            case "OnLootSpawn":
                return AdaptAction<LootContainer>(plugin, methods);
            case "CanLootEntity":
            case "CanPickupEntity":
                return AdaptFunc<BasePlayer, BaseEntity>(plugin, methods);
            case "OnLootEntityEnd":
            case "OnLootEntity":
            case "OnRocketLaunched":
                return AdaptAction<BasePlayer, BaseEntity>(plugin, methods);
            case "OnPlayerLootEnd":
                return AdaptAction<PlayerLoot>(plugin, methods);
            case "OnSamSiteTargetScan":
                return AdaptAction<SamSite, List<SamSite.ISamSiteTarget>>(plugin, methods);
            case "CanExplosiveStick":
                return AdaptFunc<TimedExplosive, BaseEntity>(plugin, methods);
            case "OnPlayerConnected":
                return AdaptAction<BasePlayer>(plugin, methods);
            case "OnPlayerDisconnected":
                return HasArity(methods, 2)
                    ? AdaptAction<BasePlayer, string>(plugin, methods)
                    : AdaptAction<BasePlayer>(plugin, methods);
            case "OnItemHeld":
                return AdaptAction<Item, BasePlayer>(plugin, methods);
            case "CanBuild":
                return AdaptFunc<Planner, Construction, Construction.Target>(plugin, methods);
            case "OnEntityBuilt":
                return AdaptAction<Planner, GameObject>(plugin, methods);
            case "OnPayForPlacement":
                return AdaptFunc<BasePlayer, Planner, Construction>(plugin, methods);
            case "OnEntityKill":
                return ReturnsObject(methods)
                    ? AdaptFunc<BaseNetworkable>(plugin, methods)
                    : AdaptAction<BaseNetworkable>(plugin, methods);
            case "OnHammerHit":
                return AdaptFunc<BasePlayer, HitInfo>(plugin, methods);
            case "OnRecyclerToggle":
                return AdaptFunc<Recycler, BasePlayer>(plugin, methods);
            case "OnItemDeployed":
                return AdaptAction<Deployer>(plugin, methods);
            case "OnMagazineReload":
                return AdaptFunc<BaseProjectile, int, BasePlayer>(plugin, methods);
            case "OnLoseCondition":
                return AdaptAction<Item, float>(plugin, methods);
            case "OnPlayerTick":
                return AdaptAction<BasePlayer, PlayerTick, bool>(plugin, methods);
            case "OnStructureRepair":
                return ReturnsObject(methods)
                    ? AdaptFunc<BaseCombatEntity, BasePlayer>(plugin, methods)
                    : WrapVoidAsFunc<BaseCombatEntity, BasePlayer>(plugin, methods);
            case "OnEntityGroundMissing":
                return AdaptFunc<BaseEntity>(plugin, methods);
            case "OnEntityDeath":
                return AdaptAction<BaseCombatEntity, HitInfo>(plugin, methods);
            case "OnServerSave":
                return AdaptAction(plugin, methods);
            case "OnMessagePlayer":
                return AdaptFunc<string, BasePlayer>(plugin, methods);
            default:
                return null;
        }
    }

    private static bool HasArity(MethodInfo[] methods, int n)
    {
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].GetParameters().Length == n)
                return true;
        }
        return false;
    }

    private static bool ReturnsObject(MethodInfo[] methods)
    {
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].ReturnType != typeof(void))
                return true;
        }
        return false;
    }

    private static bool Exact(MethodInfo mi, params Type[] args)
    {
        var parameters = mi.GetParameters();
        if (parameters.Length != args.Length)
            return false;
        for (int i = 0; i < args.Length; i++)
        {
            if (parameters[i].ParameterType != args[i])
                return false;
        }
        return true;
    }

    private static Delegate TryExact<T>(object plugin, MethodInfo[] methods, params Type[] args) where T : Delegate
    {
        for (int i = 0; i < methods.Length; i++)
        {
            if (!Exact(methods[i], args))
                continue;
            try
            {
                var created = Delegate.CreateDelegate(typeof(T), plugin, methods[i], false);
                if (created != null)
                    return created;
            }
            catch { }
        }
        return null;
    }

    private static Delegate AdaptAction(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Action>(plugin, methods, Type.EmptyTypes);
        if (exact != null)
            return exact;
        var copy = methods;
        return (Action)(() => InvokeBest(plugin, copy));
    }

    private static Delegate AdaptAction<T1>(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Action<T1>>(plugin, methods, typeof(T1));
        if (exact != null)
            return exact;
        var copy = methods;
        return (Action<T1>)(a => InvokeBest(plugin, copy, a));
    }

    private static Delegate AdaptAction<T1, T2>(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Action<T1, T2>>(plugin, methods, typeof(T1), typeof(T2));
        if (exact != null)
            return exact;
        var copy = methods;
        return (Action<T1, T2>)((a, b) => InvokeBest(plugin, copy, a, b));
    }

    private static Delegate AdaptAction<T1, T2, T3>(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Action<T1, T2, T3>>(plugin, methods, typeof(T1), typeof(T2), typeof(T3));
        if (exact != null)
            return exact;
        var copy = methods;
        return (Action<T1, T2, T3>)((a, b, c) => InvokeBest(plugin, copy, a, b, c));
    }

    private static Delegate AdaptFunc<T1>(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Func<T1, object>>(plugin, methods, typeof(T1));
        if (exact != null)
            return exact;
        var copy = methods;
        return (Func<T1, object>)(a => InvokeBest(plugin, copy, a));
    }

    private static Delegate AdaptFunc<T1, T2>(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Func<T1, T2, object>>(plugin, methods, typeof(T1), typeof(T2));
        if (exact != null)
            return exact;
        var copy = methods;
        return (Func<T1, T2, object>)((a, b) => InvokeBest(plugin, copy, a, b));
    }

    private static Delegate AdaptFunc<T1, T2, T3>(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Func<T1, T2, T3, object>>(plugin, methods, typeof(T1), typeof(T2), typeof(T3));
        if (exact != null)
            return exact;
        var copy = methods;
        return (Func<T1, T2, T3, object>)((a, b, c) => InvokeBest(plugin, copy, a, b, c));
    }

    private static Delegate AdaptFunc<T1, T2, T3, T4, T5>(object plugin, MethodInfo[] methods)
    {
        var exact = TryExact<Func<T1, T2, T3, T4, T5, object>>(plugin, methods, typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5));
        if (exact != null)
            return exact;
        var copy = methods;
        return (Func<T1, T2, T3, T4, T5, object>)((a, b, c, d, e) => InvokeBest(plugin, copy, a, b, c, d, e));
    }

    private static Delegate WrapVoidAsFunc<T1, T2>(object plugin, MethodInfo[] methods)
    {
        var action = AdaptAction<T1, T2>(plugin, methods) as Action<T1, T2>;
        if (action == null)
            return null;
        return (Func<T1, T2, object>)((a, b) =>
        {
            action(a, b);
            return null;
        });
    }

    private static object InvokeBest(object plugin, MethodInfo[] methods, params object[] args)
    {
        for (int i = 0; i < methods.Length; i++)
        {
            var parameters = methods[i].GetParameters();
            if (parameters.Length != args.Length)
                continue;
            bool match = true;
            for (int j = 0; j < parameters.Length; j++)
            {
                var value = args[j];
                var parameterType = parameters[j].ParameterType;
                if (value == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                    {
                        match = false;
                        break;
                    }
                }
                else if (!parameterType.IsInstanceOfType(value))
                {
                    match = false;
                    break;
                }
            }
            if (!match)
                continue;
            try
            {
                return methods[i].Invoke(plugin, args);
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogWarning("[GrimmCore] " + methods[i].Name + ": " + (ex.InnerException ?? ex).Message);
                return null;
            }
        }
        return null;
    }
}
