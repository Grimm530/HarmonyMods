using System;
using System.Reflection;
using UnityEngine;

namespace RustVehiclesGUIHarmony
{
    /// <summary>
    /// Resolves the Oxide PluginReference targets (RustVehicles, VehicleLicence, Economics,
    /// ServerRewards, ServerPanel) against Harmony ports published on the AppDomain.
    /// Bridges are cached per wrapper instance because the GUI compares plugin references
    /// (CorePlugin == RustVehicles) rather than names.
    /// </summary>
    public static class PluginBridges
    {
        private sealed class AppDomainCallBridge : Plugin
        {
            private readonly object _wrapper;
            private readonly MethodInfo _call;

            public AppDomainCallBridge(object wrapper, MethodInfo call, string name)
            {
                _wrapper = wrapper;
                _call = call;
                Name = name;
                IsLoaded = true;
            }

            internal object Wrapper => _wrapper;

            public override object Call(string method, params object[] args)
            {
                if (_wrapper == null || _call == null) return null;
                try
                {
                    return _call.Invoke(_wrapper, new object[] { method, args ?? Array.Empty<object>() });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RustVehiclesGUI] Bridge {Name}.Call({method}): " +
                                     (ex.InnerException?.Message ?? ex.Message));
                    return null;
                }
            }
        }

        private static readonly System.Collections.Generic.Dictionary<string, AppDomainCallBridge> Cache =
            new System.Collections.Generic.Dictionary<string, AppDomainCallBridge>(StringComparer.Ordinal);

        private static object GetDomain(string key)
        {
            try { return AppDomain.CurrentDomain.GetData(key); }
            catch { return null; }
        }

        private static Plugin Wrap(string appDomainKey, string name)
        {
            var wrapper = GetDomain(appDomainKey);
            if (wrapper == null)
            {
                lock (Cache) Cache.Remove(appDomainKey);
                return null;
            }

            lock (Cache)
            {
                if (Cache.TryGetValue(appDomainKey, out var cached) && ReferenceEquals(cached.Wrapper, wrapper))
                    return cached;
            }

            var call = wrapper.GetType().GetMethod("Call",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(string), typeof(object[]) }, null);
            if (call == null) return null;

            var bridge = new AppDomainCallBridge(wrapper, call, name);
            lock (Cache) Cache[appDomainKey] = bridge;
            return bridge;
        }

        public static Plugin RustVehicles => Wrap("RustVehicles_Plugin", "RustVehicles");
        public static Plugin VehicleLicence => Wrap("VehicleLicence_Plugin", "VehicleLicence");
        public static Plugin Economics => Wrap("Economics_Plugin", "Economics");
        public static Plugin ServerRewards => Wrap("RustRewards_Plugin", "ServerRewards");
        public static Plugin ServerPanel => Wrap("ServerPanel_Plugin", "ServerPanel");

        public static void Clear()
        {
            lock (Cache) Cache.Clear();
        }

        public static Plugin Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name.ToLowerInvariant())
            {
                case "rustvehicles": return RustVehicles;
                case "vehiclelicence": return VehicleLicence;
                case "economics": return Economics;
                case "serverrewards": return ServerRewards;
                case "serverpanel": return ServerPanel;
                default: return null;
            }
        }
    }
}
