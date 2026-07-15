using System;
using System.Reflection;
using UnityEngine;

namespace RemoverToolHarmony
{
    /// <summary>
    /// Resolves the optional Oxide plugin references used by RemoverTool against Harmony ports
    /// exposed through AppDomain wrappers. Returns null when a dependency is not present so the
    /// plugin's existing null-checks fall back gracefully (matches Oxide PluginReference behaviour).
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

            public override object Call(string method, params object[] args)
            {
                if (_wrapper == null || _call == null) return null;
                try
                {
                    return _call.Invoke(_wrapper, new object[] { method, args ?? Array.Empty<object>() });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RemoverTool] Bridge {Name}.Call({method}): " + (ex.InnerException?.Message ?? ex.Message));
                    return null;
                }
            }
        }

        private static AppDomainCallBridge Wrap(string appDomainKey, string name)
        {
            object wrapper;
            try { wrapper = AppDomain.CurrentDomain.GetData(appDomainKey); }
            catch { wrapper = null; }
            if (wrapper == null) return null;

            var call = wrapper.GetType().GetMethod("Call",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(string), typeof(object[]) }, null);
            if (call == null) return null;

            return new AppDomainCallBridge(wrapper, call, name);
        }

        // Economics -> EconomicsHarmony wrapper (Balance / Withdraw / Deposit ...).
        public static Plugin Economics => Wrap("Economics_Plugin", "Economics");

        // ServerRewards -> RustRewards Harmony wrapper if present (CheckPoints / TakePoints / AddPoints).
        public static Plugin ServerRewards => Wrap("RustRewards_Plugin", "ServerRewards");

        // Not ported to Harmony yet — optional, resolve to null (graceful).
        public static Plugin Friends => null;
        public static Plugin Clans => null;
        public static Plugin ImageLibrary => null;
        public static Plugin BuildingOwners => null;
        public static Plugin RustTranslationAPI => null;
        public static Plugin NoEscape => null;

        public static Plugin Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name.ToLowerInvariant())
            {
                case "economics": return Economics;
                case "serverrewards": return ServerRewards;
                case "friends": return Friends;
                case "clans": return Clans;
                case "imagelibrary": return ImageLibrary;
                case "buildingowners": return BuildingOwners;
                case "rusttranslationapi": return RustTranslationAPI;
                case "noescape": return NoEscape;
                default: return null;
            }
        }
    }
}
