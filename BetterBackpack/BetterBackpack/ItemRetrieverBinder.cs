using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// Resolves the ItemRetriever Harmony mod via AppDomain (same keys as Backpacks).
/// No assembly reference — Facepunch HarmonyLoader renames DLLs on load.
/// </summary>
internal static class ItemRetrieverBinder
{
    public const string AppDomainApiKey = "ItemRetriever_ApiType";
    public const string AppDomainReadyCallbacksKey = "ItemRetriever_ReadyCallbacks";
    public const string AppDomainGenerationKey = "ItemRetriever_Generation";

    private static int _boundGen = -1;
    private static Type _apiType;
    private static MethodInfo _callApi;
    private static MethodInfo _registerReady;
    private static bool _loggedLink;

    public static bool IsReady
    {
        get
        {
            EnsureBound();
            return _callApi != null;
        }
    }

    public static void RegisterReadyCallback(Action callback)
    {
        if (callback == null) return;
        EnsureBound();
        try
        {
            if (_registerReady != null)
            {
                _registerReady.Invoke(null, new object[] { callback });
                return;
            }
        }
        catch { }

        try
        {
            var list = AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) as IList;
            if (list == null)
            {
                list = new List<Action>();
                AppDomain.CurrentDomain.SetData(AppDomainReadyCallbacksKey, list);
            }
            lock (list)
            {
                if (!list.Contains(callback))
                    list.Add(callback);
            }
        }
        catch { }

        if (IsReady)
        {
            try { callback(); }
            catch { }
        }
    }

    public static object CallApi(string method, params object[] args)
    {
        EnsureBound();
        if (_callApi == null || string.IsNullOrEmpty(method))
            return null;
        try
        {
            return _callApi.Invoke(null, new object[] { method, args ?? Array.Empty<object>() });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterBackpack] ItemRetriever.Call: " + ex.Message);
            return null;
        }
    }

    private static int ReadGeneration()
    {
        try
        {
            if (AppDomain.CurrentDomain.GetData(AppDomainGenerationKey) is int g)
                return g;
        }
        catch { }
        return 0;
    }

    private static Type ResolveApiType()
    {
        var fromDomain = AppDomain.CurrentDomain.GetData(AppDomainApiKey) as Type;
        if (fromDomain != null) return fromDomain;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType("ItemRetrieverHarmony.ItemRetrieverHarmonyMod");
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private static void EnsureBound()
    {
        int gen = ReadGeneration();
        if (_callApi != null && _boundGen == gen && _apiType != null)
            return;

        try
        {
            _apiType = ResolveApiType();
            if (_apiType == null)
            {
                _callApi = null;
                _registerReady = null;
                return;
            }

            var instanceProp = _apiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp?.GetValue(null) == null)
            {
                _callApi = null;
                _registerReady = null;
                return;
            }

            _callApi = _apiType.GetMethod("CallApi", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(object[]) }, null);
            _registerReady = _apiType.GetMethod("RegisterReadyCallback", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Action) }, null);
            _boundGen = gen;

            if (!_loggedLink)
            {
                _loggedLink = true;
                Debug.Log("[BetterBackpack] Linked to ItemRetriever for worn-backpack retrieve.");
            }
        }
        catch (Exception ex)
        {
            _callApi = null;
            _registerReady = null;
            Debug.LogWarning("[BetterBackpack] ItemRetriever bind failed: " + ex.Message);
        }
    }
}
