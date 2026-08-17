using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ItemRetrieverHarmony
{
    /// <summary>
    /// Harmony entry point for ItemRetriever 0.7.7. Exposes API via AppDomain and a Plugin bridge for Backpacks.
    /// </summary>
    public class ItemRetrieverHarmonyMod : IHarmonyModHooks
    {
        public static ItemRetrieverHarmonyMod Instance { get; private set; }

        public const int VersionMajor = 0;
        public const int VersionMinor = 7;
        public const int VersionPatch = 8;

        public static readonly VersionNumber Version = new VersionNumber(VersionMajor, VersionMinor, VersionPatch);

        public const string AppDomainApiKey = "ItemRetriever_ApiType";
        public const string AppDomainPluginKey = "ItemRetriever_Plugin";
        public const string AppDomainReadyCallbacksKey = "ItemRetriever_ReadyCallbacks";
        public const string AppDomainGenerationKey = "ItemRetriever_Generation";

        private ItemRetriever _plugin;
        private Plugin _pluginBridge;
        private static int _generation;

        public ItemRetriever Plugin => _plugin;

        /// <summary>Plugin-compatible wrapper (Call routes to APIs). Assignable to other mods' PluginReference fields.</summary>
        public Plugin PluginBridge => _pluginBridge;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            ItemRetrieverHost.Init(root);
            _plugin = new ItemRetriever();
            ItemRetrieverHost.Instance.Plugin = _plugin;
            _pluginBridge = new Plugin
            {
                Name = "ItemRetriever",
                Title = "Item Retriever",
                IsLoaded = true,
                BoundInstance = _plugin
            };
            RegisterApiType();
            _plugin.HarmonyInit();
            ScheduleServerInitialized();
            NotifyReady();
            Debug.Log($"[ItemRetriever Harmony] Loaded v{VersionMajor}.{VersionMinor}.{VersionPatch}");
            Debug.Log("[ItemRetriever Harmony] Library mod - no config/data. AppDomain key: ItemRetriever_ApiType");
        }

        private void ScheduleServerInitialized(int attempt = 0)
        {
            if (_plugin == null) return;
            try
            {
                bool itemsReady = ItemManager.itemList != null && ItemManager.itemList.Count > 0;
                if (itemsReady)
                {
                    try
                    {
                        _plugin.HarmonyServerInitialized();
                        Debug.Log($"[ItemRetriever Harmony] Server initialized (v{VersionMajor}.{VersionMinor}.{VersionPatch})");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[ItemRetriever Harmony] HarmonyServerInitialized failed: " + ex);
                    }
                    return;
                }
            }
            catch { }

            if (attempt > 120)
            {
                Debug.LogWarning("[ItemRetriever Harmony] Timed out waiting for ItemManager; initializing anyway");
                try { _plugin.HarmonyServerInitialized(); }
                catch (Exception ex) { Debug.LogError("[ItemRetriever Harmony] Init failed: " + ex); }
                return;
            }

            float delay = attempt < 10 ? 0.5f : 1f;
            if (ServerMgr.Instance != null)
                ServerMgr.Instance.Invoke(() => ScheduleServerInitialized(attempt + 1), delay);
            else
            {
                try
                {
                    var go = new GameObject("ItemRetrieverHarmony_InitWait");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<InitWaitBehaviour>().Begin(this, attempt);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ItemRetriever Harmony] ScheduleServerInitialized: " + ex.Message);
                }
            }
        }

        private class InitWaitBehaviour : MonoBehaviour
        {
            private ItemRetrieverHarmonyMod _mod;
            private int _attempt;
            public void Begin(ItemRetrieverHarmonyMod mod, int attempt)
            {
                _mod = mod;
                _attempt = attempt;
                StartCoroutine(Wait());
            }
            private IEnumerator Wait()
            {
                yield return new WaitForSeconds(0.5f);
                var mod = _mod;
                var attempt = _attempt;
                Destroy(gameObject);
                mod?.ScheduleServerInitialized(attempt + 1);
            }
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            _plugin?.HarmonyUnload();
            UnregisterApiType();
            ItemRetrieverHost.Shutdown();
            _plugin = null;
            _pluginBridge = null;
            Instance = null;
        }

        private static void RegisterApiType()
        {
            try
            {
                _generation++;
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, typeof(ItemRetrieverHarmonyMod));
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, Instance?._pluginBridge);
                AppDomain.CurrentDomain.SetData(AppDomainGenerationKey, _generation);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ItemRetriever Harmony] RegisterApiType: " + ex.Message);
            }
        }

        private static void UnregisterApiType()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(AppDomainApiKey, null);
                AppDomain.CurrentDomain.SetData(AppDomainPluginKey, null);
            }
            catch { }
        }

        private static void NotifyReady()
        {
            try
            {
                var list = AppDomain.CurrentDomain.GetData(AppDomainReadyCallbacksKey) as IList;
                if (list == null) return;
                List<Action> copy;
                lock (list)
                {
                    copy = new List<Action>();
                    foreach (var item in list)
                    {
                        if (item is Action a)
                            copy.Add(a);
                    }
                }
                foreach (var a in copy)
                {
                    try { a(); }
                    catch (Exception ex) { Debug.LogWarning("[ItemRetriever Harmony] Ready callback: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ItemRetriever Harmony] NotifyReady: " + ex.Message);
            }
        }

        /// <summary>Other mods can register to be notified when ItemRetriever loads (or reloads).</summary>
        public static void RegisterReadyCallback(Action callback)
        {
            if (callback == null) return;
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

            if (Instance?._plugin != null)
            {
                try { callback(); }
                catch { }
            }
        }

        // ---- Static API (AppDomain consumers) ----

        public static object API_GetApi() => CallApi(nameof(API_GetApi));

        public static void API_AddSupplier(object pluginOrName, Dictionary<string, object> spec) =>
            CallApi(nameof(API_AddSupplier), pluginOrName, spec);

        public static void API_RemoveSupplier(object pluginOrName) =>
            CallApi(nameof(API_RemoveSupplier), pluginOrName);

        public static object API_HasContainer(BasePlayer player, ItemContainer container) =>
            CallApi(nameof(API_HasContainer), player, container);

        public static void API_AddContainer(object pluginOrName, BasePlayer player, IItemContainerEntity containerEntity,
            ItemContainer container, Func<object, BasePlayer, ItemContainer, bool> canUseContainer = null)
        {
            Func<Plugin, BasePlayer, ItemContainer, bool> wrapped = null;
            if (canUseContainer != null)
                wrapped = (p, bp, c) => canUseContainer(p?.BoundInstance ?? p, bp, c);
            CallApi(nameof(API_AddContainer), WrapPlugin(pluginOrName), player, containerEntity, container, wrapped);
        }

        public static void API_RemoveContainer(object pluginOrName, BasePlayer player, ItemContainer container) =>
            CallApi(nameof(API_RemoveContainer), pluginOrName, player, container);

        public static void API_RemoveAllContainersForPlayer(object pluginOrName, BasePlayer player) =>
            CallApi(nameof(API_RemoveAllContainersForPlayer), pluginOrName, player);

        public static void API_RemoveAllContainersForPlugin(object pluginOrName) =>
            CallApi(nameof(API_RemoveAllContainersForPlugin), pluginOrName);

        public static void API_FindPlayerItems(BasePlayer player, Dictionary<string, object> itemQuery, List<Item> collect) =>
            CallApi(nameof(API_FindPlayerItems), player, itemQuery, collect);

        public static object API_SumPlayerItems(BasePlayer player, Dictionary<string, object> itemQuery) =>
            CallApi(nameof(API_SumPlayerItems), player, itemQuery);

        public static object API_TakePlayerItems(BasePlayer player, Dictionary<string, object> itemQuery, int amount, List<Item> collect) =>
            CallApi(nameof(API_TakePlayerItems), player, itemQuery, amount, collect);

        public static void API_FindPlayerAmmo(BasePlayer player, Rust.AmmoTypes ammoType, List<Item> collect) =>
            CallApi(nameof(API_FindPlayerAmmo), player, ammoType, collect);

        public static Plugin GetPluginBridge() => Instance?._pluginBridge;

        public static object CallApi(string method, params object[] args)
        {
            var plugin = Instance?._plugin;
            if (plugin == null || string.IsNullOrEmpty(method)) return null;

            try
            {
                args = CoerceArgs(method, args);
                var mi = typeof(ItemRetriever).GetMethod(method,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning("[ItemRetriever] Unknown API method: " + method);
                    return null;
                }
                return mi.Invoke(plugin, args ?? Array.Empty<object>());
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogWarning("[ItemRetriever] " + method + ": " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ItemRetriever] " + method + ": " + ex.Message);
                return null;
            }
        }

        private static object[] CoerceArgs(string method, object[] args)
        {
            if (args == null || args.Length == 0) return args;

            // Methods whose first parameter is Plugin (supplier / container owner identity)
            bool needsPlugin =
                method == nameof(API_AddSupplier) ||
                method == nameof(API_RemoveSupplier) ||
                method == nameof(API_AddContainer) ||
                method == nameof(API_RemoveContainer) ||
                method == nameof(API_RemoveAllContainersForPlayer) ||
                method == nameof(API_RemoveAllContainersForPlugin);

            if (needsPlugin && args.Length > 0 && args[0] != null && !(args[0] is Plugin))
            {
                var copy = (object[])args.Clone();
                copy[0] = WrapPlugin(args[0]);
                return copy;
            }

            return args;
        }

        internal static Plugin WrapPlugin(object pluginOrName)
        {
            if (pluginOrName == null) return null;
            if (pluginOrName is Plugin p) return p;
            if (pluginOrName is string name)
                return new Plugin { Name = name, Title = name, IsLoaded = true };

            string resolved = null;
            try
            {
                var prop = pluginOrName.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                resolved = prop?.GetValue(pluginOrName) as string;
            }
            catch { }

            if (string.IsNullOrEmpty(resolved))
                resolved = pluginOrName.GetType().Name;

            return new Plugin
            {
                Name = resolved,
                Title = resolved,
                IsLoaded = true,
                BoundInstance = pluginOrName
            };
        }
    }
}
