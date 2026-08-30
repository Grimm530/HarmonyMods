using System;
using System.Reflection;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// Optional bind to the virtual Backpacks Harmony mod (AppDomain Backpacks_ApiType).
/// </summary>
internal static class VirtualBackpackApi
{
    private const string AppDomainApiKey = "Backpacks_ApiType";

    internal static bool HasMatchingItem(BasePlayer player, Item item)
    {
        if (player == null || item?.info == null)
            return false;
        var plugin = GetPlugin();
        if (plugin == null)
            return false;
        try
        {
            var amount = Invoke(plugin, "API_GetBackpackItemAmount", (ulong)player.userID, item.info.itemid, item.skin);
            if (ToInt(amount) > 0)
                return true;
            if (item.skin != 0)
            {
                amount = Invoke(plugin, "API_GetBackpackItemAmount", (ulong)player.userID, item.info.itemid, 0UL);
                if (ToInt(amount) > 0)
                    return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterBackpack] Virtual backpack amount check: " + ex.Message);
        }
        return false;
    }

    internal static bool TryDeposit(BasePlayer player, Item item)
    {
        if (player == null || item == null)
            return false;
        var plugin = GetPlugin();
        if (plugin == null)
            return false;
        try
        {
            Invoke(plugin, "API_PauseBackpackGatherMode", (ulong)player.userID, 1f);
            var result = Invoke(plugin, "API_TryDepositBackpackItem", (ulong)player.userID, item);
            return ToBool(result);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterBackpack] Virtual backpack deposit: " + ex.Message);
            return false;
        }
    }

    internal static bool IsVirtualBackpackContainer(ItemContainer container)
    {
        if (container == null)
            return false;
        var plugin = GetPlugin();
        if (plugin == null)
            return false;
        try
        {
            var owner = Invoke(plugin, "API_GetBackpackOwnerId", container);
            return ToUlong(owner) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static object GetPlugin()
    {
        try
        {
            var apiType = AppDomain.CurrentDomain.GetData(AppDomainApiKey) as Type;
            if (apiType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        apiType = asm.GetType("BackpacksHarmony.BackpacksHarmonyMod");
                        if (apiType != null)
                            break;
                    }
                    catch { }
                }
            }
            if (apiType == null)
                return null;
            var instance = apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance == null)
                return null;
            return apiType.GetProperty("Plugin", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static object Invoke(object plugin, string method, params object[] args)
    {
        var type = plugin.GetType();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        MethodInfo match = null;
        for (int i = 0; i < methods.Length; i++)
        {
            var m = methods[i];
            if (m.Name != method)
                continue;
            var ps = m.GetParameters();
            if (ps.Length < args.Length)
                continue;
            if (ParametersCompatible(ps, args))
            {
                match = m;
                break;
            }
        }
        if (match == null)
            return null;

        if (match.GetParameters().Length > args.Length)
        {
            var full = new object[match.GetParameters().Length];
            Array.Copy(args, full, args.Length);
            for (int i = args.Length; i < full.Length; i++)
                full[i] = Type.Missing;
            return match.Invoke(plugin, full);
        }
        return match.Invoke(plugin, args);
    }

    private static bool ParametersCompatible(ParameterInfo[] ps, object[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == null)
                continue;
            var pt = ps[i].ParameterType;
            if (pt.IsInstanceOfType(args[i]))
                continue;
            if (pt == typeof(ulong) && args[i] is ulong)
                continue;
            if (pt == typeof(float) && args[i] is float)
                continue;
            if (pt == typeof(int) && args[i] is int)
                continue;
            return false;
        }
        return true;
    }

    private static int ToInt(object value)
    {
        if (value == null)
            return 0;
        try { return Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static bool ToBool(object value)
    {
        if (value is bool b)
            return b;
        try { return Convert.ToBoolean(value); }
        catch { return false; }
    }

    private static ulong ToUlong(object value)
    {
        if (value == null)
            return 0;
        try { return Convert.ToUInt64(value); }
        catch { return 0; }
    }
}
